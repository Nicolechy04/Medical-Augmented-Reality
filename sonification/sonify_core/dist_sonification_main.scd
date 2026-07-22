// SuperCollider server for the distance-based pipeline. Python updates the
// synth and recording state through the OSC addresses defined below.
(
s.waitForBoot({

	// One synth combines region-dependent timbre, deformation cues, and the
	// distance-controlled pulse envelope into a stereo signal.
	SynthDef(\unifiedPulse, {
		|out = 0,
		freq = 440,
		pulseFreq = 2.0,
		gain = -120,
		harmonicity = 1.0,
		modIndex = 0.0,
		timbre = 0.0
		|
		var lagTime = 0.1;
		var modFreq, modulator, carrier;
		var cutoffMult, rq, noiseMix;
		var pulse, sig;

		freq = freq.lag(lagTime);
		pulseFreq = pulseFreq.lag(lagTime);
		gain = gain.lag(lagTime);
		harmonicity = harmonicity.lag(lagTime);
		modIndex = modIndex.lag(lagTime);
		timbre = timbre.lag(0.3);

		// FM synthesis core
		modFreq = freq * harmonicity;
		modulator = SinOsc.ar(modFreq) * modIndex * modFreq;
		carrier = SinOsc.ar(freq + modulator);
		carrier = tanh(carrier * 1.3);

		// Timbre: filter character varies by tissue type
		// vitreous(0)=open+clean, ILM(1)=focused, retina(2)=resonant, RPE(3)=tight+noisy
		cutoffMult = LinLin.kr(timbre, 0, 3, 8.0, 2.0);
		rq = LinLin.kr(timbre, 0, 3, 1.0, 0.3);
		noiseMix = LinLin.kr(timbre, 0, 3, 0.0, 0.15);

		sig = carrier * (1 - noiseMix) + (WhiteNoise.ar * noiseMix);
		sig = RLPF.ar(sig, (freq * cutoffMult).clip(200, 12000), rq);

		// Pulse envelope (rhythm from distance)
		pulse = LFTri.kr(pulseFreq).range(0, 1);
		pulse = pulse.pow(3);
		pulse = pulse * (pulse > 0.15);

		sig = sig * pulse * gain.dbamp;
		Out.ar(out, sig ! 2);
	}).add;

	s.sync;

	~son = Synth(\unifiedPulse);

	// Parameter order must match DistanceBasedSonification.update_params().
	OSCdef(\updateParams, { |msg|
		var freq = msg[1];
		var pulseFreq = msg[2];
		var gain = msg[3];
		var harmonicity = msg[4];
		var modIndex = msg[5];
		var timbre = msg[6];
		~son.set(
			\freq, freq,
			\pulseFreq, pulseFreq,
			\gain, gain,
			\harmonicity, harmonicity,
			\modIndex, modIndex,
			\timbre, timbre
		);
	}, '/son/update');

	// Recording is written to a temporary WAV that Python later moves to a run.
	OSCdef(\recStart, {
		fork {
			var path = "/tmp/rec_son.wav";
			"[REC] Start".postln;
			s.prepareForRecord(path);
			s.sync;
			s.record;
		};
	}, '/rec/start');

	OSCdef(\recStop, {
		fork {
			"[REC] Stop".postln;
			s.sync;
			s.stopRecording;
		};
	}, '/rec/stop');

	"[SC] Ready for unified sonification + recording".postln;

});
)
