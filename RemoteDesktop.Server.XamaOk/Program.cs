﻿using System;
using System.Windows.Forms;

namespace RemoteDesktop.Server
{

	static class Program
	{

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

		[STAThread]
		static void Main(string[] args)
		{
			// parse args
			bool isDebugMode = false;
			//int port = 8888;
			//foreach (var arg in args)
			//{
			//	if (arg == "debug") isDebugMode = true;
			//	else if (arg.StartsWith("port=")) port = int.Parse(arg.Split('=')[1]);
			//}

			// start app
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

            AllocConsole();

			if (isDebugMode) Application.Run(new MainForm());
			else Application.Run(new MainApplicationContext());
		}
	}
}
