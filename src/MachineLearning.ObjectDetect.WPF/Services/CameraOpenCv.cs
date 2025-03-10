﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenCvSharp;

namespace MachineLearning.ObjectDetect.WPF.Services
{
    public static class CameraOpenCvEvents
    {
        public delegate void GrabContinuousStartedEventHandler();
        public delegate void GrabContinuousStoppedEventHandler();
    }

    public sealed class CameraOpenCv : IDisposable, INotifyPropertyChanged
    {
#pragma warning disable CS0067 // Event used by Fody
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067 // Event used by Fody
        public record CameraDevice(int CameraIndex, string Name, string DeviceId);
        public record ImageGrabbedData(Mat image, double CurrentFPS);
        /// <summary>
        /// Enumerates the connected cameras.
        /// </summary>
        public static IEnumerable<CameraDevice> EnumerateAllConnectedCameras() =>
            DirectShowLib.DsDevice.GetDevicesOfCat(DirectShowLib.FilterCategory.VideoInputDevice).Select((x, cameraIndex) => new CameraOpenCv.CameraDevice(cameraIndex++, x.Name, x.DevicePath));

        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _fpsStopWatch = new System.Diagnostics.Stopwatch();

        public ISubject<ImageGrabbedData> ImageGrabbed { get; } = new Subject<ImageGrabbedData>();
        public event CameraOpenCvEvents.GrabContinuousStartedEventHandler? GrabContinuousStarted;
        public event CameraOpenCvEvents.GrabContinuousStoppedEventHandler? GrabContinuousStopped;
        public bool IsGrabbing { get; private set; }

        /// <summary>
        /// Frame rate used to display video.
        /// </summary>
        public int MaxDisplayFrameRate { get; set; } = 30;

        public bool FlipImageY { get; set; }
        public bool FlipImageX { get; set; }
        public double CurrentFPS { get; private set; }

        public async Task GrabContinuous_Start(int cameraIndex)
        {
            _cts = new CancellationTokenSource();
            await Task.Run(() => GrabContinuous_Proc(cameraIndex, _cts.Token), _cts.Token);
        }

        private async Task GrabContinuous_Proc(int cameraIndex, CancellationToken cancellationToken)
        {
            // FPS delay
            var fpsMilliseconds = 1000 / MaxDisplayFrameRate;

            // Init capture
            using var videoCapture = new VideoCapture(cameraIndex);
            videoCapture.Open(cameraIndex);
            if (!videoCapture.IsOpened()) throw new Exception("Could not open camera.");

            // TODO: Refactor this to Thread or in Caller
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsGrabbing = true;
            });
            GrabContinuousStarted?.Invoke();

            var fpsCounter = new List<double>();

            // Grab
            using var frame = new Mat();
            while (!cancellationToken.IsCancellationRequested)
            {
                // Reduce the number of displayed images to a reasonable amount if the camera is acquiring images very fast.
                if (!_fpsStopWatch.IsRunning || _fpsStopWatch.ElapsedMilliseconds > fpsMilliseconds)
                {
                    // Display FPS counter
                    if (_fpsStopWatch.IsRunning)
                    {
                        fpsCounter.Add(1000 / _fpsStopWatch.ElapsedMilliseconds);
                        if (fpsCounter.Count > MaxDisplayFrameRate / 2)
                        {
                            CurrentFPS = fpsCounter.Average();
                            fpsCounter.Clear();
                        }
                    }

                    _fpsStopWatch.Restart();

                    // Get frame
                    videoCapture.Read(frame);

                    if (!frame.Empty())
                    {
                        // Optional flip
                        Mat workFrame = FlipImageY ? frame.Flip(FlipMode.Y) : frame;
                        workFrame = FlipImageX ? workFrame.Flip(FlipMode.X) : workFrame;

                        if (!cancellationToken.IsCancellationRequested)
                        {
                            ImageGrabbed.OnNext(new ImageGrabbedData(workFrame, CurrentFPS));
                        }
                    }
                }
                else
                {
                    // Display frame rate speed to get desired display frame rate. We use half the expected time to consider time spent executing other code
                    var fpsDelay = (fpsMilliseconds / 2) - (int)_fpsStopWatch.ElapsedMilliseconds;
                    if (fpsDelay > 0) await Task.Delay(fpsDelay, CancellationToken.None);
                }
            }

            videoCapture.Release();
            // TODO: Refactor this to Thread or in Caller
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsGrabbing = false;
                CurrentFPS = 0;
            });
            GrabContinuousStopped?.Invoke();
        }

        public async Task GrabContinuous_Stop()
        {
            // Check
            if (_cts is null) return;

            // If "Dispose" gets called before Stop
            if (_cts.IsCancellationRequested) return;

            _cts.Cancel();

            // Wait for grab to finish - TODO: Refactor this!
            await Task.Delay(250);
        }

        public void Dispose()
        {
            _cts?.Cancel();
        }
    }
}
