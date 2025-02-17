// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using ControlzEx.Native;
using ControlzEx.Standard;
using MahApps.Metro.Controls;
using Microsoft.Xaml.Behaviors;

namespace MahApps.Metro.Behaviors
{
    public class WindowsSettingBehavior : Behavior<MetroWindow>
    {
        /// <inheritdoc />
        protected override void OnAttached()
        {
            base.OnAttached();

            this.AssociatedObject.SourceInitialized += this.AssociatedObject_SourceInitialized;
        }

        /// <inheritdoc />
        protected override void OnDetaching()
        {
            this.CleanUp("from OnDetaching");
            base.OnDetaching();
        }

        private void AssociatedObject_SourceInitialized(object? sender, EventArgs e)
        {
            this.LoadWindowState();

            var window = this.AssociatedObject;
            if (window is null)
            {
                // if the associated object is null at this point, then there is really something wrong!
                Trace.TraceError($"{this}: Can not attach to nested events, cause the AssociatedObject is null.");
                return;
            }

            window.StateChanged += this.AssociatedObject_StateChanged;
            window.Closing += this.AssociatedObject_Closing;
            window.Closed += this.AssociatedObject_Closed;

            // This operation must be thread safe. It is possible, that the window is running in a different Thread.
            Application.Current?.BeginInvoke(app =>
                {
                    if (app != null)
                    {
                        app.SessionEnding += this.CurrentApplicationSessionEnding;
                    }
                });
        }

        private void AssociatedObject_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            this.SaveWindowState();
        }

        private void AssociatedObject_Closed(object? sender, EventArgs e)
        {
            this.CleanUp("from AssociatedObject closed event");
        }

        private void CurrentApplicationSessionEnding(object? sender, SessionEndingCancelEventArgs e)
        {
            this.SaveWindowState();
        }

        private void AssociatedObject_StateChanged(object? sender, EventArgs e)
        {
            // save the settings on this state change, because hidden windows gets no window placements
            // all the saving stuff could be so much easier with ReactiveUI :-D
            if (this.AssociatedObject?.WindowState == WindowState.Minimized)
            {
                this.SaveWindowState();
            }
        }

        private void CleanUp(string fromWhere)
        {
            var window = this.AssociatedObject;
            if (window is null)
            {
                // it's bad if the associated object is null, so trace this here
                Trace.TraceWarning($"{this}: Can not clean up {fromWhere}, cause the AssociatedObject is null. This can maybe happen if this Behavior was already detached.");
                return;
            }

            Debug.WriteLine($"{this}: Clean up {fromWhere}.");

            window.StateChanged -= this.AssociatedObject_StateChanged;
            window.Closing -= this.AssociatedObject_Closing;
            window.Closed -= this.AssociatedObject_Closed;
            window.SourceInitialized -= this.AssociatedObject_SourceInitialized;

            // This operation must be thread safe
            Application.Current?.BeginInvoke(app =>
                {
                    if (app != null)
                    {
                        app.SessionEnding -= this.CurrentApplicationSessionEnding;
                    }
                });
        }

#pragma warning disable 618
        private void LoadWindowState()
        {
            var window = this.AssociatedObject;
            if (window is null)
            {
                return;
            }

            var settings = window.GetWindowPlacementSettings();
            if (settings is null || !window.SaveWindowPosition)
            {
                return;
            }

            try
            {
                settings.Reload();
            }
            catch (Exception e)
            {
                Trace.TraceError($"{this}: The settings for {window} could not be reloaded! {e}");
                return;
            }

            // check for existing placement and prevent empty bounds
            if (settings.Placement is null || settings.Placement.normalPosition.IsEmpty)
            {
                return;
            }

            try
            {
                var wp = settings.Placement;
                WinApiHelper.SetWindowPlacement(window, wp);
            }
            catch (Exception ex)
            {
                throw new MahAppsException($"Failed to set the window state for {window} from the settings.", ex);
            }
        }

        private void SaveWindowState()
        {
            var window = this.AssociatedObject;
            if (window is null)
            {
                return;
            }

            var settings = window.GetWindowPlacementSettings();
            if (settings is null || !window.SaveWindowPosition)
            {
                return;
            }

            var windowHandle = new WindowInteropHelper(window).EnsureHandle();
            var wp = NativeMethods.GetWindowPlacement(windowHandle);

            // check for saveable values
            if (wp.showCmd != SW.HIDE && wp.length > 0)
            {
                if (wp.showCmd == SW.NORMAL)
                {
                    if (UnsafeNativeMethods.GetWindowRect(windowHandle, out var rect))
                    {
                        var monitor = NativeMethods.MonitorFromWindow(windowHandle, MonitorOptions.MONITOR_DEFAULTTONEAREST);
                        if (monitor != IntPtr.Zero)
                        {
                            var monitorInfo = NativeMethods.GetMonitorInfo(monitor);
                            rect.Offset(monitorInfo.rcMonitor.Left - monitorInfo.rcWork.Left, monitorInfo.rcMonitor.Top - monitorInfo.rcWork.Top);
                        }

                        wp.normalPosition = rect;
                    }
                }

                if (!wp.normalPosition.IsEmpty)
                {
                    settings.Placement = wp;
                }
            }

            try
            {
                settings.Save();
            }
            catch (Exception e)
            {
                Trace.TraceError($"{this}: The settings could not be saved! {e}");
            }
        }
#pragma warning restore 618
    }
}