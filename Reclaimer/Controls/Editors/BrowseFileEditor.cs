﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Reclaimer.Controls.Editors
{
    public class BrowseFileEditor : BrowseEditorBase
    {
        protected override void ShowDialog()
        {
            var dir = string.Empty;
            try
            {
                dir = Directory.GetParent(PropertyItem.Value?.ToString()).FullName;
            }
            catch { }

            var ofd = new OpenFileDialog
            {
                InitialDirectory = dir,
                Multiselect = false,
                CheckFileExists = true
            };

            if (ofd.ShowDialog(Application.Current.MainWindow) == true)
                PropertyItem.Value = ofd.FileName.Replace(Settings.AppBaseDirectory, ".\\");
        }
    }
}
