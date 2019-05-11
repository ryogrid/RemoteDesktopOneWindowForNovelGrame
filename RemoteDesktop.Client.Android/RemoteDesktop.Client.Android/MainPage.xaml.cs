﻿using System;
using Xamarin.Forms;

namespace RemoteDesktop.Client.Android
{
    public partial class MainPage : MasterDetailPage
    {
        public MainPage()
        {
            InitializeComponent();

            masterPage.listView.ItemSelected += OnItemSelected;


            if (Device.RuntimePlatform == Device.UWP)
            {
                MasterBehavior = MasterBehavior.Popover;
            }
        }

        void OnItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            var item = e.SelectedItem as MasterPageItem;
            if (item != null)
            {
                Detail = new NavigationPage((Page)Activator.CreateInstance(item.TargetType));
                masterPage.listView.SelectedItem = null;
                IsPresented = false;
            }
        }

        public void pageMoveToSettings()
        {
            Detail = new NavigationPage((Page)Activator.CreateInstance(typeof(SettingPage)));
            masterPage.listView.SelectedItem = null;
            IsPresented = false;         
        }
    }
}