/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AchievementCaretaker.App.Views
{
    /// <summary>
    /// Minimal stand-in for the WinForms MessageBox, which Avalonia has no
    /// equivalent of.
    /// </summary>
    internal sealed class MessageWindow : Window
    {
        private MessageWindow(string title, string message, bool confirm)
        {
            this.Title = title;
            this.SizeToContent = SizeToContent.WidthAndHeight;
            this.CanResize = false;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // This window doubles as the main window when the Steam handshake
            // fails, so it has to be reachable from the taskbar.
            this.ShowInTaskbar = true;

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            if (confirm == true)
            {
                Button yes = new() { Content = "Yes", MinWidth = 88, IsDefault = true };
                Button no = new() { Content = "No", MinWidth = 88, IsCancel = true };
                yes.Click += (_, _) => this.Close(true);
                no.Click += (_, _) => this.Close(false);
                buttons.Children.Add(yes);
                buttons.Children.Add(no);
            }
            else
            {
                Button ok = new() { Content = "OK", MinWidth = 88, IsDefault = true, IsCancel = true };
                ok.Click += (_, _) => this.Close(false);
                buttons.Children.Add(ok);
            }

            this.Content = new StackPanel()
            {
                Margin = new(20),
                Spacing = 16,
                MaxWidth = 520,
                Children =
                {
                    new TextBlock() { Text = message, TextWrapping = TextWrapping.Wrap },
                    buttons,
                },
            };
        }

        public static MessageWindow CreateError(string message)
        {
            return new("Error", message, false);
        }

        public static Task ShowErrorAsync(Window owner, string message)
        {
            return ShowAsync(owner, "Error", message);
        }

        public static Task ShowInfoAsync(Window owner, string message)
        {
            return ShowAsync(owner, "Information", message);
        }

        /// <summary>
        /// A dialog needs an owner to be shown at all, so a caller without a
        /// window gets a completed task rather than an exception.
        /// </summary>
        private static Task ShowAsync(Window owner, string title, string message)
        {
            MessageWindow window = new(title, message, false);
            return owner == null
                ? Task.CompletedTask
                : window.ShowDialog(owner);
        }

        public static Task<bool> ShowConfirmAsync(Window owner, string title, string message)
        {
            return new MessageWindow(title, message, true).ShowDialog<bool>(owner);
        }
    }
}
