﻿using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ReactiveUI;
using System;
using System.Threading.Tasks;
using QuestPatcher.Views;
using QuestPatcher.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Patching;
using System.Diagnostics;
using QuestPatcher.Core;

namespace QuestPatcher.ViewModels.Modding
{
    /// <summary>
    /// Wrapper around a mod used to display it within the UI and add some prompts.
    /// There might be a better way to do this, not completely sure.
    /// </summary>
    public class ModViewModel : ViewModelBase
    {
        public string Name => Mod.Name;
        public string Author => Mod.Porter == null ? $"(By {Mod.Author})" : $"(By {Mod.Author} - ported by {Mod.Porter})";

        public string Version => $"v{Mod.Version}";

        public string? Description => Mod.Description;
        public Bitmap? CoverImage { get; set; }

        public bool IsInstalled
        {
            get => _isToggling ? !Mod.IsInstalled : Mod.IsInstalled;
            set
            {
                if (value != Mod.IsInstalled)
                {
                    OnToggle(value);
                }
            }
        }

        public IMod Mod { get; }

        public OperationLocker Locker { get; }

        private readonly ModManager _modManager;
        private readonly PatchingManager _patchingManager;
        private readonly Window _mainWindow;

        private bool _isToggling; // Used to temporarily display the mod with the new toggle value until the toggle succeeds or fails

        public ModViewModel(IMod mod, ModManager modManager, PatchingManager patchingManager, Window mainWindow, OperationLocker locker)
        {
            Mod = mod;
            Locker = locker;
            _modManager = modManager;
            _patchingManager = patchingManager;
            _mainWindow = mainWindow;

            mod.PropertyChanged += (_, args) =>
            {
                if (!_isToggling)
                {
                    if (args.PropertyName == nameof(Mod.IsInstalled))
                    {
                        this.RaisePropertyChanged(nameof(IsInstalled));
                    }
                }
            };

            LoadCoverImage();
        }

        private async void LoadCoverImage()
        {
            try
            {
                CoverImage = new Bitmap(await Mod.OpenCover());
                this.RaisePropertyChanged(nameof(CoverImage));
            }
            catch(Exception)
            {
                // ignored
            }
        }

        private async void OnToggle(bool installed)
        {
            Locker.StartOperation();
            try
            {
                _isToggling = true;
                if (installed)
                {
                    await InstallSafely();
                }
                else
                {
                    await UninstallSafely();
                }
                await _modManager.SaveMods();
            }
            finally
            {
                Locker.FinishOperation();
                _isToggling = false;
                this.RaisePropertyChanged(nameof(IsInstalled));
            }
        }

        /// <summary>
        /// Installs the inner mod, and handles any errors.
        /// Also shows an outdated prompt for mods which aren't for the installed app version.
        /// </summary>
        private async Task InstallSafely()
        {
            Debug.Assert(_patchingManager.InstalledApp != null);
            // Check game version, and prompt if it is incorrect to avoid users installing mods that may crash their game
            if(Mod.PackageVersion != null && Mod.PackageVersion != _patchingManager.InstalledApp.Version)
            {
                DialogBuilder builder = new()
                {
                    Title = "版本不匹配的Mod",
                    Text = $"该Mod是为{Mod.PackageVersion}版本的游戏开发的，然而你当前安装的游戏版本是{_patchingManager.InstalledApp.Version}。启用这个Mod有可能会导致游戏崩溃，也有可能正常运行。"
                };
                builder.OkButton.Text = "仍然继续";

                if(!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            try
            {
                await Mod.Install();
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to install mod", ex);
            }
        }

        /// <summary>
        /// Uninstalls the mod, and handles any errors with exception dialogs
        /// </summary>
        /// <returns></returns>
        private async Task<bool> UninstallSafely()
        {
            /*
            List<Mod> dependingOn = _modManager.FindModsDependingOn(Mod, true);
            // If the mod is depended on by other installed mods, we should ask the user before uninstalling it, since these mods will fail to load without it
            // This is a bit of a mess to make it work with both a both singular and plural number of mods
            if(dependingOn.Count > 0)
            {
                bool multiple = dependingOn.Count > 1;
                StringBuilder message = new(multiple ? "The mods " : "The mod ");
                for(int i = 0; i < dependingOn.Count; i++)
                {
                    if(i > 0)
                    {
                        if(i == dependingOn.Count - 1)
                        {
                            message.Append(" and ");
                        }
                        else
                        {
                            message.Append(", ");
                        }
                    }
                    message.Append(dependingOn[i].Name);
                }
                message.Append(multiple ? " depend" : " depends");
                message.Append(" on this mod. If the mod is uninstalled, ");
                message.Append(multiple ? "these mods" : "this mod");
                message.Append(" will most likely not work");

                DialogBuilder builder = new()
                {
                    Title = "Mod Depended On",
                    Text = message.ToString()
                };
                builder.OkButton.Text = "Continue Anyway";

                if(!await builder.OpenDialogue(_mainWindow))
                {
                    return false;
                }
            }*/ // TODO: Reimplement ^^

            try
            {
                await Mod.Uninstall();
                return true;
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to uninstall mod", ex);
                return false;
            }
        }

        public async void OnDelete()
        {
            Locker.StartOperation();
            try
            {
                // Always uninstall mods before deleting.
                // DeleteMod does this is the mod is installed, but we want to use our "safe" removal method to make sure that no mods depend on this one
                if (Mod.IsInstalled)
                {
                    if (!await UninstallSafely())
                    {
                        return;
                    }
                }

                await _modManager.DeleteMod(Mod);
                await _modManager.SaveMods();
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to delete mod", ex);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        /// <summary>
        /// Displays a dialog box with the specified exception and title.
        /// The text in the dialog will be the exception's message.
        /// The dialog will display the stack trace of the exception, unless it is an InstallationException
        /// </summary>
        /// <param name="title">Title of the dialog</param>
        /// <param name="ex">Exception to display</param>
        private async Task ShowFailDialog(string title, Exception ex)
        {
            DialogBuilder builder = new()
            {
                Title = title,
                Text = ex.Message,
                HideCancelButton = true
            };

            // InstallationExceptions are thrown by QuestPatcher itself to avoid certain conditions like installing on the wrong game
            // Displaying the stack traces for them isn't very helpful, since they aren't bugs/problems with QP
            if (ex is not InstallationException)
            {
                builder.WithException(ex);
                
            }
            if(ex is AdbException) {
                if(ex.ToString().Contains("com.beatgames.beatsaber/files/mods/"))
                {
                    builder.Text += "\n有可能可用的快速修复 点击下方按钮尝试";
                    builder.WithButtons(new ButtonInfo
                    {
                        Text = "快速修复",
                        OnClick = async () =>
                        {
                        
                            
                          
                        }
                    });
                }
            }
            await builder.OpenDialogue(_mainWindow);
        }
    }
}
