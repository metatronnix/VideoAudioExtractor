using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Dsp;

namespace VideoAudioExtractor
{
    public class BPMCalculator
    {
        private const int WindowSize = 1024;
        private const double MinBPM = 60.0;
        private const double MaxBPM = 200.0;
        private int _sampleRate;

        public double CalculateBPM(string filePath)
        {
            float[] audioData = LoadAudioFile(filePath);

            // Skip intro (first 20%) and outro (last 20%), analyze the middle 60%
            int start = audioData.Length / 5;
            int end = audioData.Length * 4 / 5;
            float[] body = audioData[start..end];

            // Split into segments, run both methods, take median of most consistent
            int numSegments = 4;
            int segmentLength = body.Length / numSegments;

            var spectralEstimates = new List<double>();
            var energyEstimates = new List<double>();

            for (int s = 0; s < numSegments; s++)
            {
                int segStart = s * segmentLength;
                int segEnd = Math.Min(segStart + segmentLength, body.Length);
                float[] segment = body[segStart..segEnd];

                spectralEstimates.Add(EstimateBPMFromOnsets(CalculateOnsetStrength(segment)));
                energyEstimates.Add(EstimateBPMFromOnsets(CalculateEnergyEnvelope(segment)));
            }

            spectralEstimates.Sort();
            energyEstimates.Sort();

            double spectralBpm = spectralEstimates[spectralEstimates.Count / 2];
            double energyBpm = energyEstimates[energyEstimates.Count / 2];

            double spectralVar = Variance(spectralEstimates);
            double energyVar = Variance(energyEstimates);

            return energyVar < spectralVar ? energyBpm : spectralBpm;
        }

        private double Variance(List<double> values)
        {
            double avg = values.Average();
            return values.Average(v => (v - avg) * (v - avg));
        }

        private float[] LoadAudioFile(string filePath)
        {
            using var reader = new AudioFileReader(filePath);
            _sampleRate = reader.WaveFormat.SampleRate;
            var samples = new List<float>();
            var buffer = new float[_sampleRate];
            int samplesRead;

            while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                samples.AddRange(buffer.Take(samplesRead));
            }

            return samples.ToArray();
        }

        private double[] CalculateOnsetStrength(float[] audioData)
        {
            int hopSize = WindowSize / 4;
            int numFrames = (audioData.Length - WindowSize) / hopSize + 1;
            double[] onsetStrength = new double[numFrames];

            Complex[] fftBuffer = new Complex[WindowSize];
            double[] previousMagnitudes = new double[WindowSize / 2];

            for (int i = 0; i < numFrames; i++)
            {
                int startIdx = i * hopSize;

                for (int j = 0; j < WindowSize; j++)
                {
                    if (startIdx + j < audioData.Length)
                    {
                        double window = 0.54 - 0.46 * Math.Cos(2 * Math.PI * j / (WindowSize - 1));
                        fftBuffer[j].X = (float)(audioData[startIdx + j] * window);
                        fftBuffer[j].Y = 0;
                    }
                    else
                    {
                        fftBuffer[j].X = 0;
                        fftBuffer[j].Y = 0;
                    }
                }

                FastFourierTransform.FFT(true,
                    (int)Math.Log(WindowSize, 2), fftBuffer);

                double flux = 0.0;
                for (int k = 1; k < WindowSize / 2; k++)
                {
                    double magnitude = Math.Sqrt(fftBuffer[k].X * fftBuffer[k].X +
                                                fftBuffer[k].Y * fftBuffer[k].Y);

                    double diff = magnitude - previousMagnitudes[k];
                    if (diff > 0)
                        flux += diff;

                    previousMagnitudes[k] = magnitude;
                }

                onsetStrength[i] = flux;
            }

            return onsetStrength;
        }

        private double[] CalculateEnergyEnvelope(float[] audioData)
        {
            int hopSize = WindowSize / 4;
            int numFrames = (audioData.Length - WindowSize) / hopSize + 1;
            double[] energy = new double[numFrames];

            for (int i = 0; i < numFrames; i++)
            {
                int startIdx = i * hopSize;
                double sum = 0;
                for (int j = 0; j < WindowSize && startIdx + j < audioData.Length; j++)
                {
                    float sample = audioData[startIdx + j];
                    sum += sample * sample;
                }
                energy[i] = Math.Sqrt(sum / WindowSize);
            }

            // Compute first-order difference (energy change), half-wave rectified
            double[] diff = new double[numFrames];
            for (int i = 1; i < numFrames; i++)
            {
                double d = energy[i] - energy[i - 1];
                diff[i] = d > 0 ? d : 0;
            }

            return diff;
        }

        private double EstimateBPMFromOnsets(double[] onsetStrength)
        {
            double mean = onsetStrength.Average();
            double[] centered = onsetStrength.Select(v => v - mean).ToArray();

            double hopTime = (double)WindowSize / 4 / _sampleRate;

            int minLag = (int)(60.0 / MaxBPM / hopTime);
            int maxLag = (int)(60.0 / MinBPM / hopTime);
            maxLag = Math.Min(maxLag, centered.Length - 1);

            if (minLag >= maxLag || maxLag >= centered.Length)
                return 120.0;

            double[] corrs = new double[maxLag + 1];
            double bestCorr = double.MinValue;
            int bestLag = minLag;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double corr = 0;
                int count = centered.Length - lag;
                for (int i = 0; i < count; i++)
                    corr += centered[i] * centered[i + lag];
                corr /= count;
                corrs[lag] = corr;

                if (corr > bestCorr)
                {
                    bestCorr = corr;
                    bestLag = lag;
                }
            }

            // Parabolic interpolation for sub-lag precision
            double preciseLag = bestLag;
            if (bestLag > minLag && bestLag < maxLag)
            {
                double left = corrs[bestLag - 1];
                double center = corrs[bestLag];
                double right = corrs[bestLag + 1];
                double denom = 2.0 * (2.0 * center - left - right);
                if (Math.Abs(denom) > 1e-10)
                    preciseLag = bestLag + (left - right) / denom;
            }

            double bestBPM = 60.0 / (preciseLag * hopTime);

            // Octave correction: always double if below 80 BPM
            if (bestBPM < 80)
            {
                bestBPM *= 2;
            }
            // Check if double-tempo has strong correlation
            else
            {
                int halfLag = bestLag / 2;
                if (halfLag >= minLag && corrs[halfLag] > bestCorr * 0.95)
                {
                    double doubleBPM = 60.0 / (halfLag * hopTime);
                    if (doubleBPM <= MaxBPM)
                        bestBPM = doubleBPM;
                }
            }

            // If above 170, halve it
            if (bestBPM > 170)
            {
                double halfBPM = bestBPM / 2.0;
                if (halfBPM >= MinBPM)
                    bestBPM = halfBPM;
            }

            return Math.Max(MinBPM, Math.Min(MaxBPM, Math.Round(bestBPM)));
        }

        private double[] SmoothOnsets(double[] data, int windowSize)
        {
            double[] result = new double[data.Length];
            int half = windowSize / 2;
            for (int i = 0; i < data.Length; i++)
            {
                double sum = 0;
                int count = 0;
                for (int j = Math.Max(0, i - half); j <= Math.Min(data.Length - 1, i + half); j++)
                {
                    sum += data[j];
                    count++;
                }
                result[i] = sum / count;
            }
            return result;
        }
    }
}
