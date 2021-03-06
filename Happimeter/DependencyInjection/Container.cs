﻿using System;
using Happimeter.Core.Database;
using Happimeter.Core.Helper;
using Happimeter.Core.Services;
using Happimeter.Interfaces;
using Happimeter.Services;

namespace Happimeter.DependencyInjection
{
    public class Container
    {

        public static void RegisterElements()
        {
            ServiceLocator.Instance.Register<IRestService, RestService>();
            ServiceLocator.Instance.Register<IHappimeterApiService, HappimeterApiService>();
            ServiceLocator.Instance.Register<IAccountStoreService, AccountStoreService>();
            ServiceLocator.Instance.Register<IBluetoothService, BluetoothService>();
            ServiceLocator.Instance.Register<ISharedDatabaseContext, SharedDatabaseContext>();
            ServiceLocator.Instance.Register<IMeasurementService, MeasurementService>();
            ServiceLocator.Instance.Register<IConfigService, ConfigService>();
            ServiceLocator.Instance.Register<ILoggingService, LoggingService>();
            ServiceLocator.Instance.Register<IPredictionService, PredictionService>();
            ServiceLocator.Instance.Register<IProximityService, ProximityService>();
            ServiceLocator.Instance.Register<IGeoLocationService, GeoLocationService>();
            ServiceLocator.Instance.Register<IGenericQuestionService, GenericQuestionService>();
            ServiceLocator.Instance.Register<ITeamService, TeamService>();
            ServiceLocator.Instance.Register<ISynchronizationService, SynchronizationService>();
            ServiceLocator.Instance.Register<INotificationService, NotificationService>();
        }
    }
}