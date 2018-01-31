﻿using System;
using UIKit;

namespace Happimeter.iOS
{
    public partial class TabBarController : UITabBarController
    {
        public TabBarController(IntPtr handle) : base(handle)
        {
            TabBar.Items[0].Title = "Browse";
            TabBar.Items[1].Title = "About";
            TabBar.Items[1].Title = "Bluetooth";
        }
    }
}
