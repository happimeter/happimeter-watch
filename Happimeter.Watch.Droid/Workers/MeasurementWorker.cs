﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Hardware;
using Android.Locations;
using Android.Runtime;
using Happimeter.Core.Database;
using Happimeter.Core.ExtensionMethods;
using Happimeter.Core.Helper;
using Happimeter.Watch.Droid.Services;
using System.Threading;
using Android.OS;
using Happimeter.Core.Models.Bluetooth;
using Happimeter.Watch.Droid.ServicesBusinessLogic;
using Android.Content.PM;
using AltBeaconOrg.Bluetooth;
using System.ServiceProcess;
using Happimeter.Core.Services;

namespace Happimeter.Watch.Droid.Workers
{
    public class MeasurementWorker : AbstractWorker
    {
        public ConcurrentBag<(double, double, double)> AccelerometerMeasures = new ConcurrentBag<(double, double, double)>();
        public ConcurrentBag<(double, double, double)> AccelerometerMagnitudeMeasures = new ConcurrentBag<(double, double, double)>();
        public ConcurrentBag<double> HeartRateMeasures = new ConcurrentBag<double>();
        public ConcurrentBag<double> StepMeasures = new ConcurrentBag<double>();
        public ConcurrentBag<double> LightMeasures = new ConcurrentBag<double>();

        public int StepsFromLastMeasurement;

        private SensorManager _sensorManager { get; set; }
        private SensorListener _accelerometerListener { get; set; }
        private SensorListener _heartRateListener { get; set; }
        private SensorListener _stepListener { get; set; }
        private SensorListener _lightListener { get; set; }

        private CancellationTokenSource _cancelationTokenSource { get; set; }

        private Context _context;
        public Context Context
        {
            get => _context ?? BackgroundService.ServiceContext;
            set
            {
                _context = value;
            }
        }

        private readonly IAudioFeaturesService _audioFeatureService;
        private readonly bool _playServicesReady;
        private FusedLocationProviderClient fusedLocationProviderClient;
        private ActivityRecognitionClient activityRecognitionClient;
        private LocationManager _locationManager;
        private readonly string locationProvider;
        private PendingIntent ActivityDetectionPendingIntent
        {
            get
            {
                var intent = new Intent(Context, typeof(DetectedActivityIntentService));
                return PendingIntent.GetService(Context, 0, intent, PendingIntentFlags.UpdateCurrent);
            }
        }

        private MeasurementWorker(Context context = null)
        {
            Context = context;
            _playServicesReady = IsGooglePlayServicesInstalled();
            _audioFeatureService = ServiceLocator.Instance.Get<IAudioFeaturesService>();
            if (_playServicesReady)
            {
                fusedLocationProviderClient = LocationServices.GetFusedLocationProviderClient(Context);
                activityRecognitionClient = ActivityRecognition.GetClient(Context);
            }
        }

        private static MeasurementWorker Instance { get; set; }

        public static MeasurementWorker GetInstance(Context context = null)
        {
            if (Instance == null)
            {
                Instance = new MeasurementWorker(context);
            }
            return Instance;
        }

        private Location GetLastKnownLocation()
        {
            _locationManager = (LocationManager)Application.Context.GetSystemService(Context.LocationService);
            var providers = _locationManager.GetProviders(true);
            Location bestLocation = null;
            foreach (var provider in providers)
            {
                Location l = _locationManager.GetLastKnownLocation(provider);
                if (l == null)
                {
                    continue;
                }
                if (bestLocation == null || l.Accuracy < bestLocation.Accuracy)
                {
                    // Found best last known location: %s", l);
                    bestLocation = l;
                }
            }
            return bestLocation;
        }

        public async void StartFor(int seconds)
        {
            _cancelationTokenSource = new CancellationTokenSource();
            await StartSensors();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), _cancelationTokenSource.Token);
            }
            catch (System.OperationCanceledException)
            {
                Console.WriteLine("Measurement service was cancelled");
                //do not collect sensors
                return;
            }

            await CollectMeasurements();
            Stop();
        }

        public async void Start()
        {
            _cancelationTokenSource = new CancellationTokenSource();
            IsRunning = true;
            await StartSensors();
            var currentTime = DateTime.UtcNow;
            if (currentTime.Second < 29)
            {

                var delayToBeSureWeCollectDataAt30Seconds = 30 - DateTime.UtcNow.Second;
                await Task.Delay(delayToBeSureWeCollectDataAt30Seconds * 1000);
            }
            else if (currentTime.Second > 31)
            {
                var delayToBeSureWeCollectDataAt30Seconds = 60 - DateTime.UtcNow.Second;
                delayToBeSureWeCollectDataAt30Seconds += 30;
                await Task.Delay(delayToBeSureWeCollectDataAt30Seconds * 1000);
            }
            while (IsRunning)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), _cancelationTokenSource.Token);
                    await CollectMeasurements();
                    //await Task.Delay(TimeSpan.FromSeconds(30), _cancelationTokenSource.Token);
                    Console.WriteLine("Saved new Sensormeasurement");
                }
                catch (System.OperationCanceledException)
                {
                    Console.WriteLine("Measurement service was cancelled");
                }

            }
        }

        private async Task CollectMeasurements()
        {
            var sensorMeasurement = new SensorMeasurement
            {
                Timestamp = DateTime.UtcNow,
                SensorItemMeasures = new List<SensorItemMeasurement>()
            };

            Location location = null;
            if (_playServicesReady)
            {
                location = await fusedLocationProviderClient.GetLastLocationAsync();
            }

            if (location == null)
            {
                location = GetLastKnownLocation();
            }

            if (location != null)
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.LocationLat,
                    Magnitude = location.Latitude,
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.LocationLon,
                    Magnitude = location.Longitude,
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.LocationAlt,
                    Magnitude = location.Altitude,
                });
            }


            var accMeasuresToSave = AccelerometerMeasures.ToList();
            AccelerometerMeasures.Clear();
            if (accMeasuresToSave.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerX,
                    NumberOfMeasures = accMeasuresToSave.Count,
                    Average = accMeasuresToSave.Select(x => x.Item1).Average(),
                    StdDev = accMeasuresToSave.Select(x => x.Item1).StdDev(),
                    Magnitude = accMeasuresToSave.Select(x => x.Item1).Sum(),
                    Quantile1 = accMeasuresToSave.Select(x => x.Item1).Quantile1(),
                    Quantile2 = accMeasuresToSave.Select(x => x.Item1).Quantile2(),
                    Quantile3 = accMeasuresToSave.Select(x => x.Item1).Quantile3(),
                    Min = accMeasuresToSave.Select(x => x.Item1).Min(),
                    Max = accMeasuresToSave.Select(x => x.Item1).Max()
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerY,
                    NumberOfMeasures = accMeasuresToSave.Count,
                    Average = accMeasuresToSave.Select(x => x.Item2).Average(),
                    StdDev = accMeasuresToSave.Select(x => x.Item2).StdDev(),
                    Magnitude = accMeasuresToSave.Select(x => x.Item2).Sum(),
                    Quantile1 = accMeasuresToSave.Select(x => x.Item2).Quantile1(),
                    Quantile2 = accMeasuresToSave.Select(x => x.Item2).Quantile2(),
                    Quantile3 = accMeasuresToSave.Select(x => x.Item2).Quantile3(),
                    Min = accMeasuresToSave.Select(x => x.Item2).Min(),
                    Max = accMeasuresToSave.Select(x => x.Item2).Max()
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerZ,
                    NumberOfMeasures = accMeasuresToSave.Count,
                    Average = accMeasuresToSave.Select(x => x.Item3).Average(),
                    StdDev = accMeasuresToSave.Select(x => x.Item3).StdDev(),
                    Magnitude = accMeasuresToSave.Select(x => x.Item3).Sum(),
                    Quantile1 = accMeasuresToSave.Select(x => x.Item3).Quantile1(),
                    Quantile2 = accMeasuresToSave.Select(x => x.Item3).Quantile2(),
                    Quantile3 = accMeasuresToSave.Select(x => x.Item3).Quantile3(),
                    Min = accMeasuresToSave.Select(x => x.Item3).Min(),
                    Max = accMeasuresToSave.Select(x => x.Item3).Max()
                });
            }

            var accMagMeasuresToSave = AccelerometerMagnitudeMeasures.ToList();
            AccelerometerMagnitudeMeasures.Clear();
            if (accMeasuresToSave.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerMagX,
                    NumberOfMeasures = accMagMeasuresToSave.Count,
                    Average = accMagMeasuresToSave.Select(x => x.Item1).Average(),
                    StdDev = accMagMeasuresToSave.Select(x => x.Item1).StdDev(),
                    Magnitude = accMagMeasuresToSave.Select(x => x.Item1).Sum(),
                    Quantile1 = accMagMeasuresToSave.Select(x => x.Item1).Quantile1(),
                    Quantile2 = accMagMeasuresToSave.Select(x => x.Item1).Quantile2(),
                    Quantile3 = accMagMeasuresToSave.Select(x => x.Item1).Quantile3(),
                    Min = accMagMeasuresToSave.Select(x => x.Item1).Min(),
                    Max = accMagMeasuresToSave.Select(x => x.Item1).Max()
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerMagY,
                    NumberOfMeasures = accMagMeasuresToSave.Count,
                    Average = accMagMeasuresToSave.Select(x => x.Item2).Average(),
                    StdDev = accMagMeasuresToSave.Select(x => x.Item2).StdDev(),
                    Magnitude = accMagMeasuresToSave.Select(x => x.Item2).Sum(),
                    Quantile1 = accMagMeasuresToSave.Select(x => x.Item2).Quantile1(),
                    Quantile2 = accMagMeasuresToSave.Select(x => x.Item2).Quantile2(),
                    Quantile3 = accMagMeasuresToSave.Select(x => x.Item2).Quantile3(),
                    Min = accMagMeasuresToSave.Select(x => x.Item2).Min(),
                    Max = accMagMeasuresToSave.Select(x => x.Item2).Max()
                });
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.AccelerometerMagZ,
                    NumberOfMeasures = accMagMeasuresToSave.Count,
                    Average = accMagMeasuresToSave.Select(x => x.Item3).Average(),
                    StdDev = accMagMeasuresToSave.Select(x => x.Item3).StdDev(),
                    Magnitude = accMagMeasuresToSave.Select(x => x.Item3).Sum(),
                    Quantile1 = accMagMeasuresToSave.Select(x => x.Item3).Quantile1(),
                    Quantile2 = accMagMeasuresToSave.Select(x => x.Item3).Quantile2(),
                    Quantile3 = accMagMeasuresToSave.Select(x => x.Item3).Quantile3(),
                    Min = accMagMeasuresToSave.Select(x => x.Item3).Min(),
                    Max = accMagMeasuresToSave.Select(x => x.Item3).Max()
                });
            }



            var hearRateMeasuresToSave = HeartRateMeasures.ToList();
            HeartRateMeasures.Clear();
            if (hearRateMeasuresToSave.Any(x => Math.Abs(x) > 0.1))
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.HeartRate,
                    NumberOfMeasures = hearRateMeasuresToSave.Count(),
                    Average = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Average(),
                    StdDev = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).StdDev(),
                    Magnitude = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Sum(),
                    Quantile1 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Quantile1(),
                    Quantile2 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Quantile2(),
                    Quantile3 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Quantile3(),
                    Min = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Min(),
                    Max = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 0.1).Max()
                });
                if (hearRateMeasuresToSave.Any(x => Math.Abs(x) > 25))
                {
                    sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                    {
                        Type = MeasurementItemTypes.HeartRateClean,
                        NumberOfMeasures = hearRateMeasuresToSave.Count(),
                        Average = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Average(),
                        StdDev = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).StdDev(),
                        Magnitude = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Sum(),
                        Quantile1 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Quantile1(),
                        Quantile2 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Quantile2(),
                        Quantile3 = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Quantile3(),
                        Min = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Min(),
                        Max = hearRateMeasuresToSave.Where(x => Math.Abs(x) > 25).Max()
                    });
                }
            }

            var stepMeasuresToSave = StepMeasures.ToList();
            StepMeasures.Clear();

            if (stepMeasuresToSave.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.Step,
                    NumberOfMeasures = stepMeasuresToSave.Count(),
                    Magnitude = stepMeasuresToSave.Sum(),
                });
            }

            var lightMeasuresToSave = LightMeasures.ToList();
            LightMeasures.Clear();
            if (lightMeasuresToSave.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.Light,
                    NumberOfMeasures = lightMeasuresToSave.Count(),
                    Average = lightMeasuresToSave.Average(),
                    StdDev = lightMeasuresToSave.StdDev(),
                    Magnitude = lightMeasuresToSave.Sum(),
                    Quantile1 = lightMeasuresToSave.Quantile1(),
                    Quantile2 = lightMeasuresToSave.Quantile2(),
                    Quantile3 = lightMeasuresToSave.Quantile3(),
                    Min = lightMeasuresToSave.Min(),
                    Max = lightMeasuresToSave.Max()
                });
            }

            var microphoneMeasures = MicrophoneWorker.GetInstance().MicrophoneMeasures.ToList();
            MicrophoneWorker.GetInstance().MicrophoneMeasures.Clear();
            if (microphoneMeasures.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.Microphone,
                    NumberOfMeasures = microphoneMeasures.Count(),
                    Average = microphoneMeasures.Average(),
                    StdDev = microphoneMeasures.StdDev(),
                    Magnitude = microphoneMeasures.Sum(),
                    Quantile1 = microphoneMeasures.Quantile1(),
                    Quantile2 = microphoneMeasures.Quantile2(),
                    Quantile3 = microphoneMeasures.Quantile3(),
                    Min = microphoneMeasures.Min(),
                    Max = microphoneMeasures.Max()
                });
            }

            var vadMeasures = _audioFeatureService.VadMeasures.ToList();
            _audioFeatureService.VadMeasures.Clear();
            if (vadMeasures.Any())
            {
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = MeasurementItemTypes.Vad,
                    NumberOfMeasures = vadMeasures.Count(),
                    Average = vadMeasures.Average(),
                    StdDev = vadMeasures.StdDev(),
                    Magnitude = vadMeasures.Sum(),
                    Quantile1 = vadMeasures.Quantile1(),
                    Quantile2 = vadMeasures.Quantile2(),
                    Quantile3 = vadMeasures.Quantile3(),
                    Min = vadMeasures.Min(),
                    Max = vadMeasures.Max()
                });
            }


            sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
            {
                Type = MeasurementItemTypes.Vmc,
                Magnitude = -1,
            });

            var measures = DetectedActivityIntentService.Measures;
            foreach (var measure in measures)
            {
                if (measure.Value.Any())
                {
                    var measureList = measure.Value.ToList().Select(x => (double)x);
                    sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                    {
                        Type = GetActivityById(measure.Key),
                        NumberOfMeasures = measureList.Count(),
                        Average = measureList.Average(),
                        StdDev = measureList.StdDev(),
                        Magnitude = measureList.Sum(),
                        Quantile1 = measureList.Quantile1(),
                        Quantile2 = measureList.Quantile2(),
                        Quantile3 = measureList.Quantile3(),
                        Min = measureList.Min(),
                        Max = measureList.Max()
                    });
                }
            }
            DetectedActivityIntentService.Measures.Clear();

            //proximity from normal BT Scan
            var proximityMeasures = BluetoothScannerWorker.ProximityMeasures.ToList();
            var proximityByUserIds = proximityMeasures.GroupBy(x => x.Item1);
            foreach (var userIdObj in proximityByUserIds)
            {
                var userId = userIdObj.Key;
                var rssiList = userIdObj.Select(x => (double)x.Item2).ToList();
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = $"{MeasurementItemTypes.ProximityRssi}_{userId}",
                    NumberOfMeasures = rssiList.Count(),
                    Average = rssiList.Average(),
                    StdDev = rssiList.StdDev(),
                    Magnitude = rssiList.Sum(),
                    Quantile1 = rssiList.Quantile1(),
                    Quantile2 = rssiList.Quantile2(),
                    Quantile3 = rssiList.Quantile3(),
                    Min = rssiList.Min(),
                    Max = rssiList.Max()
                });
            }
            BluetoothScannerWorker.ProximityMeasures.Clear();

            var beaconProximityMeasures = BeaconListenerService.ProximityMeasures.ToList();
            var beaconProximityByUserIds = beaconProximityMeasures.GroupBy(x => x.Item1);
            foreach (var userIdObj in beaconProximityByUserIds)
            {
                var userId = userIdObj.Key;
                var distanceCmList = userIdObj.Select(x => (double)x.Item2).ToList();
                sensorMeasurement.SensorItemMeasures.Add(new SensorItemMeasurement
                {
                    Type = $"{MeasurementItemTypes.ProximityCm}_{userId}",
                    NumberOfMeasures = distanceCmList.Count(),
                    Average = distanceCmList.Average(),
                    StdDev = distanceCmList.StdDev(),
                    Magnitude = distanceCmList.Sum(),
                    Quantile1 = distanceCmList.Quantile1(),
                    Quantile2 = distanceCmList.Quantile2(),
                    Quantile3 = distanceCmList.Quantile3(),
                    Min = distanceCmList.Min(),
                    Max = distanceCmList.Max()
                });
            }
            BeaconListenerService.ProximityMeasures.Clear();

            if (hearRateMeasuresToSave.Any(x => Math.Abs(x) > 0.1)
                || ServiceLocator.Instance.Get<IConfigService>().GetSaveMeasurementIfNoHeartrate())
            {
                ServiceLocator.Instance.Get<IMeasurementService>().AddSensorMeasurement(sensorMeasurement);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("not saving measurement because no heartrate");
            }
        }

        public async void Stop()
        {
            IsRunning = false;
            _cancelationTokenSource?.Cancel();
            await StopSensors();
        }

        private async Task StopSensors()
        {
            if (_accelerometerListener != null)
            {
                _sensorManager.UnregisterListener(_accelerometerListener);
            }
            if (_heartRateListener != null)
            {
                _sensorManager.UnregisterListener(_heartRateListener);
            }
            if (_stepListener != null)
            {
                _sensorManager.UnregisterListener(_stepListener);
            }
            if (_lightListener != null)
            {
                _sensorManager.UnregisterListener(_lightListener);
            }

            if (_playServicesReady)
            {
                await activityRecognitionClient.RemoveActivityUpdatesAsync(ActivityDetectionPendingIntent);
            }

            if (_audioFeatureService.IsActive)
            {
                //_audioFeatureService.Toggle();
            }

            BluetoothScannerWorker.GetInstance().Stop();
        }

        private async Task StartSensors()
        {
            _sensorManager = (SensorManager)Application.Context.GetSystemService(Android.Content.Context.SensorService);
            await StopSensors();
            var light = _sensorManager.GetDefaultSensor(SensorType.Light);
            if (light != null)
            {
                _lightListener = new SensorListener(this);
                _sensorManager.RegisterListener(_lightListener, light, SensorDelay.Ui);
            }

            var acc = _sensorManager.GetDefaultSensor(SensorType.Accelerometer);
            if (acc != null)
            {
                _accelerometerListener = new SensorListener(this);
                _sensorManager.RegisterListener(_accelerometerListener, acc, SensorDelay.Ui);
            }

            var heartRate = _sensorManager.GetDefaultSensor(SensorType.HeartRate);
            if (heartRate != null)
            {
                _heartRateListener = new SensorListener(this);
                _sensorManager.RegisterListener(_heartRateListener, heartRate, SensorDelay.Ui);
            }

            var stepCounter = _sensorManager.GetDefaultSensor(SensorType.StepCounter);
            if (stepCounter != null)
            {
                _stepListener = new SensorListener(this);
                _sensorManager.RegisterListener(_stepListener, stepCounter, SensorDelay.Ui);
            }

            if (_playServicesReady)
            {
                await activityRecognitionClient.RequestActivityUpdatesAsync(60 * 1000, ActivityDetectionPendingIntent);
            }

            if (!_audioFeatureService.IsActive)
            {
                //_audioFeatureService.Toggle();
            }

            BluetoothScannerWorker.GetInstance().Start();
        }

        bool IsGooglePlayServicesInstalled()
        {
            var queryResult = Android.Gms.Common.GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(Context);
            if (queryResult == Android.Gms.Common.ConnectionResult.Success)
            {
                Console.WriteLine("Google Play Services is installed on this device.");
                return true;
            }

            if (GoogleApiAvailability.Instance.IsUserResolvableError(queryResult))
            {
                // Check if there is a way the user can resolve the issue
                var errorString = GoogleApiAvailability.Instance.GetErrorString(queryResult);
                Console.WriteLine($"There is a problem with Google Play Services on this device: {queryResult} - {errorString}");
                //Toast.MakeText(Context, $"There is a problem with Google Play Services on this device: {queryResult} - {errorString}", ToastLength.Long).Show();
                // Alternately, display the error to the user.
            }

            return false;
        }

        private string GetActivityById(int id)
        {
            switch (id)
            {
                case 0:
                    return MeasurementItemTypes.ActivityInCar;
                case 1:
                    return MeasurementItemTypes.ActivityOnBicycle;
                case 2:
                    return MeasurementItemTypes.ActivityOnFoot;
                case 3:
                    return MeasurementItemTypes.ActivityStill;
                case 4:
                    return MeasurementItemTypes.ActivityUnspecific;
                case 7:
                    return MeasurementItemTypes.ActivityWalking;
                case 8:
                    return MeasurementItemTypes.ActivityRunning;
                default:
                    //6 is unspecific, 5 is tilting which we don't use
                    return MeasurementItemTypes.ActivityUnspecific;
            }
        }
    }

    public class LocationListener : Java.Lang.Object, Android.Locations.ILocationListener
    {
        public void OnLocationChanged(Location location)
        {
            System.Diagnostics.Debug.WriteLine(location);
        }

        public void OnProviderDisabled(string provider)
        {
            //throw new NotImplementedException();
        }

        public void OnProviderEnabled(string provider)
        {
            //throw new NotImplementedException();
        }

        public void OnStatusChanged(string provider, [GeneratedEnum] Availability status, Bundle extras)
        {
            //throw new NotImplementedException();
        }
    }

    public class SensorListener : Java.Lang.Object, ISensorEventListener
    {
        public SensorListener(MeasurementWorker worker)
        {
            _worker = worker;
        }

        private MeasurementWorker _worker { set; get; }

        public void OnAccuracyChanged(Sensor sensor, [GeneratedEnum] SensorStatus accuracy)
        {

        }

        public void OnSensorChanged(SensorEvent e)
        {
            //Console.WriteLine($"Got Sensor event from sensor: {e.Sensor.Type} with values: {string.Concat(e.Values.Select(x => x.ToString()))}");
            if (e.Sensor.Type == SensorType.Accelerometer)
            {
                _worker.AccelerometerMeasures.Add((e.Values[0], e.Values[1], e.Values[2]));
                _worker.AccelerometerMagnitudeMeasures.Add((Math.Sqrt(Math.Pow(e.Values[0], 2)), Math.Sqrt(Math.Pow(e.Values[1], 2)), Math.Sqrt(Math.Pow(e.Values[2], 2))));
            }
            else if (e.Sensor.Type == SensorType.HeartRate)
            {
                foreach (var measure in e.Values)
                {
                    _worker.HeartRateMeasures.Add(measure);
                }
            }
            else if (e.Sensor.Type == SensorType.StepCounter)
            {
                foreach (var measure in e.Values)
                {
                    if (_worker.StepsFromLastMeasurement == 0)
                    {
                        _worker.StepsFromLastMeasurement = (int)measure;
                    }
                    else
                    {
                        _worker.StepMeasures.Add(measure - _worker.StepsFromLastMeasurement);
                        _worker.StepsFromLastMeasurement = (int)measure;
                    }
                }
            }
            else if (e.Sensor.Type == SensorType.Light)
            {
                foreach (var measure in e.Values)
                {
                    _worker.LightMeasures.Add(measure);
                }
            }
        }
    }
}
