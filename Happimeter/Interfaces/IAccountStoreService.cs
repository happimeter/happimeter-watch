﻿using System;
using Xamarin.Auth;

namespace Happimeter.Interfaces
{
    public interface IAccountStoreService
    {
        Account GetAccount();
        void DeleteAccount();
        string GetAccountToken();
        int GetAccountUserId();
        void SaveAccount(string userName, string token, int Id, DateTime expires);
        bool IsAuthenticated();
        string GetToken();
    }
}