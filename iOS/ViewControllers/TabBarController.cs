﻿using System;
using System.Linq;
using Happimeter.Views;
using UIKit;
using Xamarin.Forms;

namespace Happimeter.iOS
{
    public partial class TabBarController : UITabBarController
    {
        public TabBarController(IntPtr handle) : base(handle)
        {
            var viewControllers = ViewControllers.ToList();
            //viewControllers = viewControllers.Append(new SurveyPage().CreateViewController()).ToList();
            ViewControllers = viewControllers.ToArray();
            TabBar.Items[0].Image = UIImage.FromBundle("BluetoothIcon");
            TabBar.Items[1].Image = UIImage.FromBundle("airdrop");

            TabBar.Translucent = false;

        }
    }
}
