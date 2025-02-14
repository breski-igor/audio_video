using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenCvSharp;
using Xabe.FFmpeg;

namespace audio_video
{
    internal class Program
    {
        static string outputDirectory, fileName;
        static int deleteAfterSeconds;
        static bool recording = true;

        static async Task Main(string[] args)
        {
            while (string.IsNullOrWhiteSpace(outputDirectory))
            {
                Console.WriteLine("Unesite path za spremanje datoteke (npr. C:\\Users\\Ime\\Videos): ");
                outputDirectory = Console.ReadLine().Trim('"');
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            while (string.IsNullOrWhiteSpace(fileName))
            {
                Console.WriteLine("Unesite ime datoteke: ");
                fileName = Console.ReadLine();
            }

            Console.WriteLine("Unesite broj sati nakon kojeg će se datoteka automatski obrisati (pritisnite Enter za 1 sat): ");
            string input = Console.ReadLine();
            int deleteAfterHours = string.IsNullOrWhiteSpace(input) ? 1 : (int.TryParse(input, out int hours) ? hours : 1);
            deleteAfterSeconds = deleteAfterHours * 10;

            Console.WriteLine("Odaberite način snimanja:\n1 - Audio\n2 - Video\n3 - Audio + Video");
            string choice = Console.ReadLine();

            string audioFile = Path.Combine(outputDirectory, fileName + ".wav");
            string videoFile = Path.Combine(outputDirectory, fileName + ".avi");
            string outputFile = Path.Combine(outputDirectory, fileName + ".mp4");

            switch (choice)
            {
                case "1":
                    RecordAudio(audioFile);
                    break;
                case "2":
                    RecordVideo(videoFile);
                    break;
                case "3":
                    await RecordVideoPlusAudio(videoFile, audioFile, outputFile);
                    break;
                default:
                    Console.WriteLine("Nepoznata opcija.");
                    break;
            }

            Console.WriteLine($"Datoteke će se automatski brisati nakon {deleteAfterSeconds} sekundi.");
            StartAutoDelete();

        }

        static void RecordAudio(string audioPath)
        {
            try
            {
                using (var waveIn = new WaveInEvent())
                using (var writer = new WaveFileWriter(audioPath, waveIn.WaveFormat))
                {
                    waveIn.DataAvailable += (s, a) => writer.Write(a.Buffer, 0, a.BytesRecorded);
                    waveIn.StartRecording();

                    Console.WriteLine("Snimanje zvuka... Pritisnite Enter za prekid.");
                    Console.ReadLine();
                    waveIn.StopRecording();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška prilikom snimanja audio: " + ex.Message);
            }
        }

        static void RecordVideo(string videoPath)
        {
            try
            {
                using (var capture = new VideoCapture(0))
                using (var writer = new VideoWriter(videoPath, FourCC.XVID, 30, new OpenCvSharp.Size(capture.FrameWidth, capture.FrameHeight)))
                {
                    if (!capture.IsOpened())
                    {
                        Console.WriteLine("Nije pronađena kamera.");
                        return;
                    }

                    recording = true;

                    Task.Run(() =>
                    {
                        Console.ReadLine();
                        recording = false;
                    });

                    Console.WriteLine("Snimanje videa... Pritisnite Enter za prekid.");
                    while (recording)
                    {
                        using (Mat frame = new Mat())
                        {
                            capture.Read(frame);
                            if (frame.Empty()) break;
                            writer.Write(frame);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška prilikom snimanja videa: " + ex.Message);
            }
        }

        static async Task RecordVideoPlusAudio(string videoPath, string audioPath, string outputPath)
        {
            Console.WriteLine("Pokrećem snimanje audio i video... Pritisnite Enter za prekid.");

            var cts = new CancellationTokenSource();

            using (var waveIn = new WaveInEvent())
            using (var writer = new WaveFileWriter(audioPath, waveIn.WaveFormat))
            using (var capture = new VideoCapture(0))
            using (var videoWriter = new VideoWriter(videoPath, FourCC.XVID, 30, new OpenCvSharp.Size(capture.FrameWidth, capture.FrameHeight)))
            {
                if (!capture.IsOpened())
                {
                    Console.WriteLine("Nije pronađena kamera.");
                    return;
                }

                waveIn.DataAvailable += (s, a) => writer.Write(a.Buffer, 0, a.BytesRecorded);
                waveIn.StartRecording();

                _ = Task.Run(() =>
                {
                    Console.ReadLine();
                    cts.Cancel();
                });

                Console.WriteLine("Snimanje u tijeku... Pritisnite Enter za prekid.");
                while (!cts.Token.IsCancellationRequested)
                {
                    using (Mat frame = new Mat())
                    {
                        capture.Read(frame);
                        if (frame.Empty()) break;
                        videoWriter.Write(frame);
                    }
                }

                waveIn.StopRecording();
            }

            Console.WriteLine("Snimanje završeno. Spajam audio i video...");
            await MergeAudioVideo(videoPath, audioPath, outputPath);
            Console.WriteLine("Video snimljen: " + outputPath);
        }

        static void RecordAudioThread(string audioPath)
        {
            try
            {
                using (var waveIn = new WaveInEvent())
                using (var writer = new WaveFileWriter(audioPath, waveIn.WaveFormat))
                {
                    waveIn.DataAvailable += (s, a) => writer.Write(a.Buffer, 0, a.BytesRecorded);
                    waveIn.StartRecording();

                    while (recording) Thread.Sleep(100);

                    waveIn.StopRecording();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška kod audio snimanja: " + ex.Message);
            }
        }

        static void RecordVideoThread(string videoPath)
        {
            try
            {
                using (var capture = new VideoCapture(0))
                using (var writer = new VideoWriter(videoPath, FourCC.XVID,30, new OpenCvSharp.Size(capture.FrameWidth, capture.FrameHeight)))
                {
                    if (!capture.IsOpened())
                    {
                        Console.WriteLine("Nije pronađena kamera.");
                        return;
                    }

                    while (recording)
                    {
                        using (Mat frame = new Mat())
                        {
                            capture.Read(frame);
                            if (frame.Empty()) break;
                            writer.Write(frame);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška kod video snimanja: " + ex.Message);
            }
        }

        static async Task MergeAudioVideo(string videoPath, string audioPath, string outputPath)
        {
            try
            {
                if (!File.Exists(videoPath) || !File.Exists(audioPath))
                {
                    Console.WriteLine("Greška: Audio ili video datoteka ne postoji.");
                    return;
                }

                var videoInfo = await FFmpeg.GetMediaInfo(videoPath);
                var audioInfo = await FFmpeg.GetMediaInfo(audioPath);

                var videoStream = videoInfo.VideoStreams.FirstOrDefault();
                var audioStream = audioInfo.AudioStreams.FirstOrDefault();

                if (videoStream == null || audioStream == null)
                {
                    Console.WriteLine("Greška: Nema validnih audio/video streamova.");
                    return;
                }

                await FFmpeg.Conversions.New()
                    .AddStream(audioStream)
                    .AddStream(videoStream)
                    .SetOutput(outputPath)
                    .AddParameter("-async 1")
                    .AddParameter("-vsync 1")
                    .Start();


                File.Delete(videoPath);
                File.Delete(audioPath);

                Console.WriteLine("Spajanje završeno. Video spremljen u: " + outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška pri spajanju audio i video: " + ex.Message);
            }
        }


        static void StartAutoDelete()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(outputDirectory))
                        {
                            if (File.GetCreationTime(file).AddSeconds(deleteAfterSeconds) < DateTime.Now)
                            {
                                File.Delete(file);
                                Console.WriteLine($"Obrisan: {file}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Greška kod automatskog brisanja: " + ex.Message);
                    }

                    await Task.Delay(1000);
                }
            });
        }
    }
}
