﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AltBeaconOrg.BoundBeacon;
using Android.App;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;
using Happimeter.Core.Helper;
using Happimeter.Core.Models.Bluetooth;
using Happimeter.Watch.Droid.Bluetooth;
using Happimeter.Watch.Droid.Database;
using Happimeter.Watch.Droid.ServicesBusinessLogic;
using Java.Util;

namespace Happimeter.Watch.Droid.Workers
{
    public class BluetoothWorker : AbstractWorker
    {
        public BluetoothManager Manager;
        public BluetoothGattServer GattServer;
        //public Dictionary<string, BluetoothGatt> GattClients = new Dictionary<string, BluetoothGatt>();

        /// <summary>
        ///  For notifying the devices. Probably is not working right now
        /// </summary>
        public Dictionary<string, BluetoothDevice> SubscribedDevices = new Dictionary<string, BluetoothDevice>();

        /// <summary>
        ///     To know, how many bytes we can send in one instance.
        /// </summary>
        public Dictionary<string, int> DevicesMtu = new Dictionary<string, int>();

        /// <summary>
        ///     To be able to cancel when the stop command is hit. e.g. bluetooth gets deactivated
        /// </summary>
        /// <value>The token source.</value>
        private CancellationTokenSource TokenSource { get; set; }

        #region Singleton instanciation
        private static BluetoothWorker Instance { get; set; }
        public static BluetoothWorker GetInstance()
        {
            if (Instance == null)
            {
                Instance = new BluetoothWorker();
            }
            return Instance;
        }
        private BluetoothWorker()
        {
            Manager = (BluetoothManager)Application.Context.GetSystemService(Android.Content.Context.BluetoothService);
        }
        #endregion

        public override void Start()
        {
            //we don't need to test for bt, its done before calling this method
            TokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(() => {
                IsRunning = true;
                InitializeGatt();
                ConnectableAdvertisement();
            }, TokenSource.Token);
        }

        private void InitializeGatt() {
            GattServer = Manager.OpenGattServer(Application.Context, new CallbackGatt(this));
            GattServer.AddService(HappimeterService.Create());

            if (!ServiceLocator.Instance.Get<IDeviceService>().IsPaired()) {
                GattServer.AddService(HappimeterAuthService.Create());    
            }

            System.Diagnostics.Debug.WriteLine("Gatt initialized");
        }

        public override void Stop()
        {
            TokenSource?.Cancel(false);
            if (GattServer != null) {
                GattServer.Close();
                GattServer.Dispose();                
            }
            Manager.Adapter.BluetoothLeAdvertiser?.StopAdvertising(new CallbackAd());
            Manager.Adapter.BluetoothLeAdvertiser?.Dispose();

            IsRunning = false;
        }

        public void RemoveAuthService() {

            GattServer?.Services.ToList().ForEach((x) => Console.WriteLine(x.Uuid.ToString()));
            var authService = GattServer?.Services.Where(x => x.Uuid.ToString().ToUpper() == UuidHelper.AndroidWatchAuthServiceUuidString.ToUpper()).FirstOrDefault() ?? null;
            if (authService != null) {
                GattServer.RemoveService(authService);
                Console.WriteLine("Removed Auth Service");
            }
        }

        public void AddAuthService() {
            var authService = GattServer?.Services.FirstOrDefault(x => x.Uuid.ToString().ToUpper() == UuidHelper.AndroidWatchAuthServiceUuidString.ToUpper()) ?? null;
            if (authService == null && GattServer != null)
            {
                GattServer.AddService(HappimeterAuthService.Create());
                Console.WriteLine("Added Auth Service");
            }
        }

        private void ConnectableAdvertisement()
        {
            System.Diagnostics.Debug.WriteLine("About to start auth advertiser");
            //make sure no previous ad is running
            BluetoothAdapter.DefaultAdapter.BluetoothLeAdvertiser.StopAdvertising(new CallbackAd());
            var settings = new AdvertiseSettings.Builder()
                                                .SetAdvertiseMode(AdvertiseMode.LowLatency)
                                                .SetTxPowerLevel(AdvertiseTx.PowerHigh)
                                                .SetConnectable(true)
                                                .Build();

            var deviceName = ServiceLocator.Instance.Get<IDeviceService>().GetDeviceName();

            var tmpr = BluetoothAdapter.DefaultAdapter.SetName(deviceName);
            var userId = ServiceLocator.Instance.Get<IDatabaseContext>().Get<BluetoothPairing>(x => x.IsPairingActive)?.PairedWithUserId ?? 0;
            var data = new AdvertiseData.Builder()
                                        .SetIncludeDeviceName(true)
                                        .AddServiceUuid(ParcelUuid.FromString(UuidHelper.AndroidWatchServiceUuidString))
                                        //advertise the userId, so that phone can connect to right watch
                                        .AddServiceData(ParcelUuid.FromString(UuidHelper.AndroidWatchServiceUuidString), Encoding.UTF8.GetBytes(userId.ToString()))
                                        .Build();

            BluetoothAdapter.DefaultAdapter.BluetoothLeAdvertiser.StartAdvertising(settings, data, new CallbackAd());
            System.Diagnostics.Debug.WriteLine("stopped auth advertiser");
        }
    }



    public class CallbackAd : AdvertiseCallback
    {
        public override void OnStartSuccess(AdvertiseSettings settingsInEffect)
        {
            base.OnStartSuccess(settingsInEffect);
            System.Diagnostics.Debug.WriteLine("Started advertising");
        }

        public override void OnStartFailure(AdvertiseFailure errorCode)
        {
            base.OnStartFailure(errorCode);
            System.Diagnostics.Debug.WriteLine("advertising error  " + errorCode.ToString());
        }
    }

    public class CallbackGatt : BluetoothGattServerCallback
    {
        public BluetoothWorker Worker { get; set; }

        private Dictionary<string, bool> AuthenticationDeviceDidGreat = new Dictionary<string, bool>();
        /// <summary>
        /// The write receiver context for device.
        /// Key is a tuble of <deviceaddress>,<characteristicuuid>
        /// </summary>
        private Dictionary<(string, string), WriteReceiverContext> WriteReceiverContextForDevice = new Dictionary<(string, string), WriteReceiverContext>();

        public CallbackGatt(BluetoothWorker worker)
        {
            Worker = worker;
        }

        public override void OnConnectionStateChange(BluetoothDevice device, ProfileState status, ProfileState newState)
        {
            base.OnConnectionStateChange(device, status, newState);

            if (newState == ProfileState.Connected) {
                Console.WriteLine("device is now connected: watch as server");

                /*var client = device.ConnectGatt(Application.Context, true, new CallBackGattClient(Worker));
                if (!Worker.GattClients.ContainsKey(device.Address)) {
                    Worker.GattClients.Add(device.Address, client);
                }
                */
            }
        }

        public override void OnCharacteristicReadRequest(BluetoothDevice device, int requestId, int offset, BluetoothGattCharacteristic characteristic)
        {
            base.OnCharacteristicReadRequest(device, requestId, offset, characteristic);
            ReadHostContext context = null;
            var authCharac = characteristic as HappimeterAuthCharacteristic;
            if (authCharac != null)
            {
                context = authCharac.GetReadHostContext(device.Address);
            }

            var dataCharac = characteristic as HappimeterDataCharacteristic;
            if (dataCharac != null)
            {
                context = dataCharac.GetReadHostContext(device.Address);
            }

            if (context == null) {
                Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, new byte[] { 0x00, 0x00, 0x00 });
                return;
            }
            if (!context.DidSendHeader) {
                Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, context.Header);
                context.DidSendHeader = true;
                return;
            }

            var mtu = Worker.DevicesMtu.ContainsKey(device.Address) ? Worker.DevicesMtu[device.Address] : 20;
            var bytesToSend = context.GetNextBytes(mtu);

            if (context.Complete) {
                if (authCharac != null)
                {
                    authCharac.DoneReading(device.Address);
                }

                if (dataCharac != null)
                { 
                    dataCharac.DoneReading(device.Address);
                }
            }

            if (!bytesToSend.Any()) {
                bytesToSend = new byte[] { 0x00, 0x00, 0x00 };
            }

            Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, bytesToSend);
            return; 
        }

        public override void OnCharacteristicWriteRequest(BluetoothDevice device, int requestId, BluetoothGattCharacteristic characteristic, bool preparedWrite, bool responseNeeded, int offset, byte[] value)
        {
            base.OnCharacteristicWriteRequest(device, requestId, characteristic, preparedWrite, responseNeeded, offset, value);

            if (value.Count() == 3 && value.All(x => x == 0x00))
            {
                WriteReceiverContextForDevice.Remove((device.Address, characteristic.Uuid.ToString()));
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, value);
                }
                return;
            }
            var isInitialMessage = false;
            if (!WriteReceiverContextForDevice.ContainsKey((device.Address, characteristic.Uuid.ToString())))
            {
                isInitialMessage = true;

                //if the header is maleformated we need to catch that!
                try {
                    WriteReceiverContextForDevice.Add((device.Address, characteristic.Uuid.ToString()), new WriteReceiverContext(value));    
                } catch (Exception e) {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Failure, offset, value);
                }

            }
            var context = WriteReceiverContextForDevice[(device.Address, characteristic.Uuid.ToString())];
            var messageName = WriteReceiverContextForDevice[(device.Address, characteristic.Uuid.ToString())].MessageName;

            if (messageName != DataExchangeFirstMessage.MessageNameConstant 
                && messageName != DataExchangeConfirmationMessage.MessageNameConstant
                && messageName != AuthFirstMessage.MessageNameConstant
                && messageName != AuthSecondMessage.MessageNameConstant
                && messageName != GenericQuestionMessage.MessageNameConstant)
            {
                //Error we can't handle message
                System.Diagnostics.Debug.WriteLine($"Device {device.Address} wrote something which I don't know how to handle to data characteristic!");
                Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, Encoding.UTF8.GetBytes("wrong pass phrase!"));
                WriteReceiverContextForDevice.Remove((device.Address, characteristic.Uuid.ToString()));
                return;
            }

            if (!isInitialMessage)
            {
                if (!context.CanAddMessagePart(value)) {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Failure, offset, Encoding.UTF8.GetBytes("wrong pass phrase!"));
                    return;
                }
                context.AddMessagePart(value);
            }

            if (!context.ReadComplete)
            {
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, value);
                }
                return;
            }

            var messageJson = WriteReceiverContextForDevice[(device.Address, characteristic.Uuid.ToString())].GetMessageAsJson();
            //let's be open for new write requests
            WriteReceiverContextForDevice.Remove((device.Address, characteristic.Uuid.ToString()));

            if (messageJson == null) {
                Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Failure, offset, Encoding.UTF8.GetBytes("malformated gzip"));
                return;
            }

            //todo: handle message

            var authCharac = characteristic as HappimeterAuthCharacteristic;
            if (authCharac != null)
            {
                authCharac.HandleWriteJson(messageJson, device.Address);
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, value);
                }
                return;
            }

            var dataCharac = characteristic as HappimeterDataCharacteristic;
            if (dataCharac != null)
            {
                //dataCharac.HandleWriteAsync(device, requestId, preparedWrite, responseNeeded, offset, value, Worker);
                dataCharac.HandleWriteJson(messageJson, device.Address);
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, value);
                }
                return;
            }

            var questionCharac = characteristic as HappimeterGenericQuestionCharacteristic;
            if (questionCharac != null)
            {
                //dataCharac.HandleWriteAsync(device, requestId, preparedWrite, responseNeeded, offset, value, Worker);
                questionCharac.HandleWriteJson(messageJson, device.Address);
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, value);
                }
                return;
            }

            Worker.GattServer.SendResponse(device, requestId, Android.Bluetooth.GattStatus.Success, offset, Encoding.UTF8.GetBytes("Unknwon characteristic!"));
        }

        public override void OnDescriptorReadRequest(BluetoothDevice device, int requestId, int offset, BluetoothGattDescriptor descriptor)
        {
            base.OnDescriptorReadRequest(device, requestId, offset, descriptor);

            if (descriptor.Uuid.ToString() == UUID.FromString("00002902-0000-1000-8000-00805f9b34fb").ToString())
            {
                List<byte> valueToReturn;
                if (Worker.SubscribedDevices.ContainsKey(device.Address)) {
                    valueToReturn = BluetoothGattDescriptor.EnableNotificationValue.ToList<byte>();
                } else {
                    valueToReturn = BluetoothGattDescriptor.DisableNotificationValue.ToList<byte>();
                }
                Worker.GattServer.SendResponse(device,
                    requestId,
                                               Android.Bluetooth.GattStatus.Success,
                    0,
                    valueToReturn.ToArray());
            }

            Worker.GattServer.SendResponse(device,
                            requestId,
                            Android.Bluetooth.GattStatus.Success,
                            0,
                            null);
        }


        public override void OnDescriptorWriteRequest(BluetoothDevice device, int requestId, BluetoothGattDescriptor descriptor, bool preparedWrite, bool responseNeeded, int offset, byte[] value)
        {
            base.OnDescriptorWriteRequest(device, requestId, descriptor, preparedWrite, responseNeeded, offset, value);

            if (descriptor.Uuid.ToString() == UUID.FromString("00002902-0000-1000-8000-00805f9b34fb").ToString())
            {
                if (value.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue))
                {
                    if (!Worker.SubscribedDevices.ContainsKey(device.Address))
                    {
                        Worker.SubscribedDevices.Add(device.Address, device);
                    }

                }
                else if (value.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue))
                {
                    if (Worker.SubscribedDevices.ContainsKey(device.Address))
                    {
                        Worker.SubscribedDevices.Remove(device.Address);
                    }
                }
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device,
                            requestId,
                            Android.Bluetooth.GattStatus.Success,
                            0,
                            value);
                }
            }
            else
            {
                if (responseNeeded)
                {
                    Worker.GattServer.SendResponse(device,
                            requestId,
                            Android.Bluetooth.GattStatus.Success,
                            0,
                            null);
                }
            }
        }

        public override void OnMtuChanged(BluetoothDevice device, int mtu)
        {
            base.OnMtuChanged(device, mtu);

            if (!Worker.DevicesMtu.ContainsKey(device.Address))
            {
                Worker.DevicesMtu.Add(device.Address, mtu);
            } else {
                Worker.DevicesMtu[device.Address] = mtu;
            }
        }
    }

    public class CallBackGattClient : BluetoothGattCallback
    {

        public BluetoothWorker Worker { get; set; }

        public CallBackGattClient(BluetoothWorker worker)
        {
            Worker = worker;
        }

        public override void OnConnectionStateChange(BluetoothGatt gatt, Android.Bluetooth.GattStatus status, ProfileState newState)
        {
            base.OnConnectionStateChange(gatt, status, newState);
            Console.WriteLine("device is now connected: watch as client");
            if (newState == ProfileState.Connected) {
                var success = gatt.RequestMtu(512);
            }
        }

        public override void OnMtuChanged(BluetoothGatt gatt, int mtu, Android.Bluetooth.GattStatus status)
        {
            base.OnMtuChanged(gatt, mtu, status);

            if (!Worker.DevicesMtu.ContainsKey(gatt.Device.Address))
            {
                Worker.DevicesMtu.Add(gatt.Device.Address, mtu);
            }
            else
            {
                Worker.DevicesMtu[gatt.Device.Address] = mtu;
            }
        }
    }
}
