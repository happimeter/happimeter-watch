﻿using System;
using System.Collections.Generic;
using Happimeter.ViewModels.Forms;
using Xamarin.Forms;

namespace Happimeter.Views
{
    public partial class SignInPage : ContentPage
    {
        public SignInPage()
        {
            Resources = App.ResourceDict;
            InitializeComponent();
            BindingContext = new SignInViewModel();
        }
    }
}
