using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

public static class UInt16GrayscalePngVolumeLoader
{
    private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool TryCreateTexture3DFromFolder(string folderPath, TextureFormat textureFormat, out Texture3D texture, out string message)
    {
        texture = null;
        message = string.Empty;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            message = "UInt16 PNG folder not found: " + folderPath;
            return false;
        }

        FileInfo[] pngFiles = new DirectoryInfo(folderPath)
            .GetFiles("*.png")
            .OrderBy(f => ExtractLeadingNumber(Path.GetFileNameWithoutExtension(f.Name)))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pngFiles.Length == 0)
        {
            message = "UInt16 PNG folder contains no .png slices: " + folderPath;
            return false;
        }

        int width = 0;
        int height = 0;
        int slicePixelCount = 0;
        ushort globalMin = ushort.MaxValue;
        ushort globalMax = 0;
        Color[] volumeColors = null;

        for (int sliceIndex = 0; sliceIndex < pngFiles.Length; sliceIndex++)
        {
            ushort[] sliceValues = ReadUInt16GrayscalePng(pngFiles[sliceIndex].FullName, out int sliceWidth, out int sliceHeight, out ushort sliceMin, out ushort sliceMax);
            if (sliceValues == null)
            {
                message = "Failed to read uint16 grayscale PNG slice: " + pngFiles[sliceIndex].FullName;
                return false;
            }

            if (sliceIndex == 0)
            {
                width = sliceWidth;
                height = sliceHeight;
                slicePixelCount = width * height;
                volumeColors = new Color[slicePixelCount * pngFiles.Length];
            }
            else if (sliceWidth != width || sliceHeight != height)
            {
                message = $"Slice size mismatch in '{pngFiles[sliceIndex].FullName}'. Expected {width}x{height} but got {sliceWidth}x{sliceHeight}.";
                return false;
            }

            globalMin = Math.Min(globalMin, sliceMin);
            globalMax = Math.Max(globalMax, sliceMax);

            int dstOffset = sliceIndex * slicePixelCount;
            for (int y = 0; y < height; y++)
            {
                int flippedY = height - 1 - y;
                int srcRowOffset = y * width;
                int dstRowOffset = flippedY * width;

                for (int x = 0; x < width; x++)
                {
                    float normalized = sliceValues[srcRowOffset + x] / 65535.0f;
                    volumeColors[dstOffset + dstRowOffset + x] = new Color(normalized, 0.0f, 0.0f, 1.0f);
                }
            }
        }

        texture = new Texture3D(width, height, pngFiles.Length, textureFormat, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(volumeColors);
        texture.Apply(false, false);

        float minNormalized = globalMin / 65535.0f;
        float maxNormalized = globalMax / 65535.0f;
        message = $"Created feature Texture3D from raw uint16 PNG slices in '{folderPath}'. " +
                  $"Slices: {pngFiles.Length}, Resolution: {width}x{height}, Raw range: {globalMin}..{globalMax}, " +
                  $"Normalized range: {minNormalized:F6}..{maxNormalized:F6}.";
        return true;
    }

    private static ushort[] ReadUInt16GrayscalePng(string filePath, out int width, out int height, out ushort minValue, out ushort maxValue)
    {
        width = 0;
        height = 0;
        minValue = ushort.MaxValue;
        maxValue = 0;

        byte[] fileBytes = File.ReadAllBytes(filePath);
        if (fileBytes.Length < PngSignature.Length || !HasPngSignature(fileBytes))
        {
            return null;
        }

        byte bitDepth = 0;
        byte colorType = 0;
        byte compressionMethod = 0;
        byte filterMethod = 0;
        byte interlaceMethod = 0;
        List<byte> compressedData = new List<byte>();

        int offset = PngSignature.Length;
        while (offset + 8 <= fileBytes.Length)
        {
            int chunkLength = ReadInt32BigEndian(fileBytes, offset);
            offset += 4;

            string chunkType = Encoding.ASCII.GetString(fileBytes, offset, 4);
            offset += 4;

            if (chunkLength < 0 || offset + chunkLength + 4 > fileBytes.Length)
            {
                return null;
            }

            if (chunkType == "IHDR")
            {
                width = ReadInt32BigEndian(fileBytes, offset);
                height = ReadInt32BigEndian(fileBytes, offset + 4);
                bitDepth = fileBytes[offset + 8];
                colorType = fileBytes[offset + 9];
                compressionMethod = fileBytes[offset + 10];
                filterMethod = fileBytes[offset + 11];
                interlaceMethod = fileBytes[offset + 12];
            }
            else if (chunkType == "IDAT")
            {
                for (int i = 0; i < chunkLength; i++)
                {
                    compressedData.Add(fileBytes[offset + i]);
                }
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            offset += chunkLength + 4;
        }

        if (width <= 0 || height <= 0 || bitDepth != 16 || colorType != 0 || compressionMethod != 0 || filterMethod != 0 || interlaceMethod != 0)
        {
            return null;
        }

        byte[] inflated = InflateZlibData(compressedData.ToArray());
        if (inflated == null)
        {
            return null;
        }

        const int bytesPerPixel = 2;
        int scanlineLength = width * bytesPerPixel;
        int expectedInflatedLength = (scanlineLength + 1) * height;
        if (inflated.Length != expectedInflatedLength)
        {
            return null;
        }

        byte[] unfiltered = new byte[scanlineLength * height];
        int srcOffset = 0;
        for (int y = 0; y < height; y++)
        {
            byte filterType = inflated[srcOffset++];
            int rowOffset = y * scanlineLength;

            for (int x = 0; x < scanlineLength; x++)
            {
                byte rawValue = inflated[srcOffset++];
                byte left = x >= bytesPerPixel ? unfiltered[rowOffset + x - bytesPerPixel] : (byte)0;
                byte up = y > 0 ? unfiltered[rowOffset + x - scanlineLength] : (byte)0;
                byte upLeft = (y > 0 && x >= bytesPerPixel) ? unfiltered[rowOffset + x - scanlineLength - bytesPerPixel] : (byte)0;

                byte value;
                switch (filterType)
                {
                    case 0:
                        value = rawValue;
                        break;
                    case 1:
                        value = (byte)(rawValue + left);
                        break;
                    case 2:
                        value = (byte)(rawValue + up);
                        break;
                    case 3:
                        value = (byte)(rawValue + ((left + up) >> 1));
                        break;
                    case 4:
                        value = (byte)(rawValue + PaethPredictor(left, up, upLeft));
                        break;
                    default:
                        return null;
                }

                unfiltered[rowOffset + x] = value;
            }
        }

        ushort[] pixels = new ushort[width * height];
        for (int i = 0, src = 0; i < pixels.Length; i++, src += 2)
        {
            ushort value = (ushort)((unfiltered[src] << 8) | unfiltered[src + 1]);
            pixels[i] = value;
            minValue = Math.Min(minValue, value);
            maxValue = Math.Max(maxValue, value);
        }

        if (pixels.Length == 0)
        {
            minValue = 0;
        }

        return pixels;
    }

    private static byte[] InflateZlibData(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length < 6)
        {
            return null;
        }

        using (MemoryStream input = new MemoryStream(compressedData, 2, compressedData.Length - 6))
        using (DeflateStream inflater = new DeflateStream(input, CompressionMode.Decompress))
        using (MemoryStream output = new MemoryStream())
        {
            inflater.CopyTo(output);
            return output.ToArray();
        }
    }

    private static bool HasPngSignature(byte[] fileBytes)
    {
        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (fileBytes[i] != PngSignature[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset)
    {
        return (data[offset] << 24) |
               (data[offset + 1] << 16) |
               (data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static int ExtractLeadingNumber(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return int.MaxValue;
        }

        int end = 0;
        while (end < fileName.Length && char.IsDigit(fileName[end]))
        {
            end++;
        }

        if (end > 0 && int.TryParse(fileName.Substring(0, end), out int number))
        {
            return number;
        }

        return int.MaxValue;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        if (pb <= pc)
        {
            return b;
        }

        return c;
    }
}
