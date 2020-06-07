﻿using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CampaignSetupUI
    {
        private GUIComponent newGameContainer, loadGameContainer;

        private GUIListBox subList;
        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;

        private GUILayoutGroup subPreviewContainer;

        private GUIButton loadGameButton, deleteMpSaveButton;
        
        public Action<SubmarineInfo, string, string> StartNewGame;
        public Action<string> LoadGame;

        public GUIButton StartButton
        {
            get;
            private set;
        }

        private readonly bool isMultiplayer;

        public CampaignSetupUI(bool isMultiplayer, GUIComponent newGameContainer, GUIComponent loadGameContainer, IEnumerable<SubmarineInfo> submarines, IEnumerable<string> saveFiles = null)
        {
            this.isMultiplayer = isMultiplayer;
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;

            var columnContainer = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = isMultiplayer ? 0.0f : 0.02f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(isMultiplayer ? Vector2.Zero : new Vector2(1.5f, 1.0f), columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            columnContainer.Recalculate();

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SaveName"), font: GUI.SubHeadingFont);
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, string.Empty)
            {
                textFilterFunction = (string str) => { return ToolBox.RemoveInvalidFileNameChars(str); }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("MapSeed"), font: GUI.SubHeadingFont);
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, ToolBox.RandomSeed(8));

            if (!isMultiplayer)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SelectedSub"), font: GUI.SubHeadingFont);

                var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), isHorizontal: true)
                {
                    Stretch = true
                };

                subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.65f), leftColumn.RectTransform)) { ScrollBarVisible = true };

                var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), filterContainer.RectTransform), TextManager.Get("serverlog.filter"), textAlignment: Alignment.CenterLeft, font: GUI.Font);
                var searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform, Anchor.CenterRight), font: GUI.Font, createClearButton: true);
                filterContainer.RectTransform.MinSize = searchBox.RectTransform.MinSize;
                searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
                searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
                searchBox.OnTextChanged += (textBox, text) => { FilterSubs(subList, text); return true; };

                subList.OnSelected = OnSubSelected;
            }
            else // Spacing to fix the multiplayer campaign setup layout
            {
                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), leftColumn.RectTransform), style: null);
            }

            // New game right side
            subPreviewContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), rightColumn.RectTransform))
            {
                Stretch = true
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.13f), 
                (isMultiplayer ? leftColumn : rightColumn).RectTransform) { MaxSize = new Point(int.MaxValue, 60) }, childAnchor: Anchor.TopRight);
            if (!isMultiplayer) { buttonContainer.IgnoreLayoutGroups = true; }

            StartButton = new GUIButton(new RectTransform(isMultiplayer ? new Vector2(0.5f, 1.0f) : Vector2.One,
                buttonContainer.RectTransform, Anchor.BottomRight) { MaxSize = new Point(350, 60) }, 
                TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                    {
                        saveNameBox.Flash(GUI.Style.Red);
                        return false;
                    }

                    SubmarineInfo selectedSub = null;

                    if (!isMultiplayer)
                    {
                        if (!(subList.SelectedData is SubmarineInfo)) { return false; }
                        selectedSub = subList.SelectedData as SubmarineInfo;
                    }
                    else
                    {
                        if (GameMain.NetLobbyScreen.SelectedSub == null) { return false; }
                        selectedSub = GameMain.NetLobbyScreen.SelectedSub;
                    }

                    if (string.IsNullOrEmpty(selectedSub.MD5Hash.Hash))
                    {
                        ((GUITextBlock)subList.SelectedComponent).TextColor = Color.DarkRed * 0.8f;
                        subList.SelectedComponent.CanBeFocused = false;
                        subList.Deselect();
                        return false;
                    }

                    string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
                    bool hasRequiredContentPackages = selectedSub.RequiredContentPackagesInstalled;

                    if (selectedSub.HasTag(SubmarineTag.Shuttle) || !hasRequiredContentPackages)
                    {
                        if (!hasRequiredContentPackages)
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                                TextManager.GetWithVariable("ContentPackageMismatchWarning", "[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = msgBox.Close;
                            msgBox.Buttons[0].OnClicked += (button, obj) =>
                            {
                                if (GUIMessageBox.MessageBoxes.Count == 0)
                                {
                                    StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                                    if (isMultiplayer)
                                    {
                                        CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                                    }
                                }
                                return true;
                            };

                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                        }

                        if (selectedSub.HasTag(SubmarineTag.Shuttle))
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                                TextManager.Get("ShuttleWarning"),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = (button, obj) => 
                            {
                                StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                                if (isMultiplayer)
                                {
                                    CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                                }
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;

                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                            return false;
                        }
                    }
                    else
                    {
                        StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                        if (isMultiplayer)
                        {
                            CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                        }
                    }

                    return true;
                }
            };


            if (!isMultiplayer)
            {
                var disclaimerBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(5) }, style: "GUINotificationButton")
                {
                    IgnoreLayoutGroups = true,
                    OnClicked = (btn, userdata) => { GameMain.Instance.ShowCampaignDisclaimer(); return true; }
                };
                disclaimerBtn.RectTransform.MaxSize = new Point((int)(30 * GUI.Scale));
            }

            columnContainer.Recalculate();
            leftColumn.Recalculate();
            rightColumn.Recalculate();

            if (submarines != null) { UpdateSubList(submarines); }
            UpdateLoadMenu(saveFiles);
        }

        public void RandomizeSeed()
        {
            seedBox.Text = ToolBox.RandomSeed(8);
        }

        private void FilterSubs(GUIListBox subList, string filter)
        {
            foreach (GUIComponent child in subList.Content.Children)
            {
                var sub = child.UserData as SubmarineInfo;
                if (sub == null) { return; }
                child.Visible = string.IsNullOrEmpty(filter) ? true : sub.DisplayName.ToLower().Contains(filter.ToLower());
            }
        }

        private bool OnSubSelected(GUIComponent component, object obj)
        {
            if (subPreviewContainer == null) { return false; }
            (subPreviewContainer.Parent as GUILayoutGroup)?.Recalculate();
            subPreviewContainer.ClearChildren();

            SubmarineInfo sub = obj as SubmarineInfo;
            if (sub == null) { return true; }

            sub.CreatePreviewWindow(subPreviewContainer);
            return true;
        }

        private IEnumerable<object> WaitForCampaignSetup()
        {
            GUI.SetCursorWaiting();
            string headerText = TextManager.Get("CampaignStartingPleaseWait");
            var msgBox = new GUIMessageBox(headerText, TextManager.Get("CampaignStarting"), new string[] { TextManager.Get("Cancel") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                GameMain.NetLobbyScreen.HighlightMode(GameMain.NetLobbyScreen.SelectedModeIndex);
                GameMain.NetLobbyScreen.SelectMode(GameMain.NetLobbyScreen.SelectedModeIndex);
                GUI.ClearCursorWait();
                CoroutineManager.StopCoroutines("WaitForCampaignSetup");
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
            while (GameMain.NetLobbyScreen.CampaignUI == null && DateTime.Now < timeOut)
            {
                msgBox.Header.Text = headerText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();
            GUI.ClearCursorWait();
            yield return CoroutineStatus.Success;
        }

        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList(IEnumerable<SubmarineInfo> submarines)
        {
#if !DEBUG
            var subsToShow = submarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus));
#else
            var subsToShow = submarines;
#endif

            subList.ClearChildren();

            foreach (SubmarineInfo sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0.1f), subList.Content.RectTransform) { MinSize = new Point(0, 30) },
                    ToolBox.LimitString(sub.DisplayName, GUI.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };
                               
                if(!sub.RequiredContentPackagesInstalled)
                {
                    textBlock.TextColor = Color.Lerp(textBlock.TextColor, Color.DarkRed, .5f);
                    textBlock.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + textBlock.RawToolTip;
                }

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    textBlock.TextColor = textBlock.TextColor * 0.85f;

                    var shuttleText = new GUITextBlock(new RectTransform(new Point(100, textBlock.Rect.Height), textBlock.RectTransform, Anchor.CenterRight)
                    {
                        IsFixedSize = false
                    },
                        TextManager.Get("Shuttle", fallBackTag: "RespawnShuttle"), textAlignment: Alignment.Right, font: GUI.SmallFont)
                    {
                        TextColor = textBlock.TextColor * 0.8f,
                        ToolTip = textBlock.RawToolTip
                    };
                }
            }
            if (SubmarineInfo.SavedSubmarines.Any())
            {
                var nonShuttles = subsToShow.Where(s => !s.HasTag(SubmarineTag.Shuttle)).ToList();
                if (nonShuttles.Count > 0)
                {
                    subList.Select(nonShuttles[Rand.Int(nonShuttles.Count)]);
                }
            }
        }

        private List<string> prevSaveFiles;
        public void UpdateLoadMenu(IEnumerable<string> saveFiles = null)
        {
            prevSaveFiles?.Clear();
            loadGameContainer.ClearChildren();

            if (saveFiles == null)
            {
                saveFiles = SaveUtil.GetSaveFiles(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            }

            var leftColumn = new GUILayoutGroup(new RectTransform(isMultiplayer ? new Vector2(1.0f, 0.85f) : new Vector2(0.5f, 1.0f), loadGameContainer.RectTransform), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            saveList = new GUIListBox(new RectTransform(Vector2.One, leftColumn.RectTransform))
            {
                OnSelected = SelectSaveFile
            };

            if (!isMultiplayer)
            {
                new GUIButton(new RectTransform(new Vector2(0.6f, 0.08f), leftColumn.RectTransform), TextManager.Get("showinfolder"))
                {
                    OnClicked = (btn, userdata) =>
                    {
                        try
                        {
                            ToolBox.OpenFileWithShell(SaveUtil.SaveFolder);
                        }
                        catch (Exception e)
                        {
                            new GUIMessageBox(
                                TextManager.Get("error"), 
                                TextManager.GetWithVariables("showinfoldererror", new string[] { "[folder]", "[errormessage]" }, new string[] { SaveUtil.SaveFolder, e.Message }));
                        }
                        return true;
                    }
                };
            }

            foreach (string saveFile in saveFiles)
            {
                string fileName = saveFile;
                string subName = "";
                string saveTime = "";
                string contentPackageStr = "";
                var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) }, style: "ListBoxElement")
                {
                    UserData = saveFile
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform), "")
                {
                    CanBeFocused = false
                };

                if (!isMultiplayer)
                {
                    nameText.Text = Path.GetFileNameWithoutExtension(saveFile);
                    XDocument doc = SaveUtil.LoadGameSessionDoc(saveFile);

                    if (doc?.Root == null)
                    {
                        DebugConsole.ThrowError("Error loading save file \"" + saveFile + "\". The file may be corrupted.");
                        nameText.TextColor = GUI.Style.Red;
                        continue;
                    }
                    if (doc.Root.GetChildElement("multiplayercampaign") != null)
                    {
                        //multiplayer campaign save in the wrong folder -> don't show the save
                        saveList.Content.RemoveChild(saveFrame);
                        continue;
                    }
                    subName =  doc.Root.GetAttributeString("submarine", "");
                    saveTime = doc.Root.GetAttributeString("savetime", "");
                    contentPackageStr = doc.Root.GetAttributeString("selectedcontentpackages", "");

                    prevSaveFiles?.Add(saveFile);
                }
                else
                {
                    string[] splitSaveFile = saveFile.Split(';');
                    saveFrame.UserData = splitSaveFile[0];
                    fileName = nameText.Text = Path.GetFileNameWithoutExtension(splitSaveFile[0]);
                    prevSaveFiles?.Add(fileName);
                    if (splitSaveFile.Length > 1) { subName = splitSaveFile[1]; }
                    if (splitSaveFile.Length > 2) { saveTime = splitSaveFile[2]; }
                    if (splitSaveFile.Length > 3) { contentPackageStr = splitSaveFile[3]; }
                }
                if (!string.IsNullOrEmpty(saveTime) && long.TryParse(saveTime, out long unixTime))
                {
                    DateTime time = ToolBox.Epoch.ToDateTime(unixTime);
                    saveTime = time.ToString();
                }
                if (!string.IsNullOrEmpty(contentPackageStr))
                {
                    List<string> contentPackagePaths = contentPackageStr.Split('|').ToList();
                    if (!GameSession.IsCompatibleWithSelectedContentPackages(contentPackagePaths, out string errorMsg))
                    {
                        nameText.TextColor = GUI.Style.Red;
                        saveFrame.ToolTip = string.Join("\n", errorMsg, TextManager.Get("campaignmode.contentpackagemismatchwarning"));
                    }
                }

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                    text: subName, font: GUI.SmallFont)
                {
                    CanBeFocused = false,
                    UserData = fileName
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                    text: saveTime, textAlignment: Alignment.Right, font: GUI.SmallFont)
                {
                    CanBeFocused = false,
                    UserData = fileName
                };
            }

            saveList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                string file1 = c1.GUIComponent.UserData as string;
                string file2 = c2.GUIComponent.UserData as string;
                DateTime file1WriteTime = DateTime.MinValue;
                DateTime file2WriteTime = DateTime.MinValue;
                try
                {
                    file1WriteTime = File.GetLastWriteTime(file1);
                }
                catch
                { 
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                try
                {
                    file2WriteTime = File.GetLastWriteTime(file2);
                }
                catch
                {
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                return file2WriteTime.CompareTo(file1WriteTime);
            });

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomRight), TextManager.Get("LoadButton"))
            {
                OnClicked = (btn, obj) =>
                {
                    if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) { return false; }
                    LoadGame?.Invoke(saveList.SelectedData as string);
                    if (isMultiplayer)
                    {
                        CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                    }
                    return true;
                },
                Enabled = false
            };
            deleteMpSaveButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomLeft), 
                TextManager.Get("Delete"), style: "GUIButtonSmall")
            {
                OnClicked = DeleteSave,
                Visible = false
            };
        }       
        
        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;

            if (isMultiplayer)
            {
                loadGameButton.Enabled = true;
                deleteMpSaveButton.Visible = deleteMpSaveButton.Enabled = GameMain.Client.IsServerOwner;
                deleteMpSaveButton.Enabled = GameMain.GameSession?.SavePath != fileName;
                if (deleteMpSaveButton.Visible)
                {
                    deleteMpSaveButton.UserData = obj as string;
                }
                return true;
            }

            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);
            if (doc == null)
            {
                DebugConsole.ThrowError("Error loading save file \"" + fileName + "\". The file may be corrupted.");
                return false;
            }

            loadGameButton.Enabled = true;

            RemoveSaveFrame();

            string subName = doc.Root.GetAttributeString("submarine", "");
            string saveTime = doc.Root.GetAttributeString("savetime", "unknown");
            if (long.TryParse(saveTime, out long unixTime))
            {
                DateTime time = ToolBox.Epoch.ToDateTime(unixTime);
                saveTime = time.ToString();
            }

            string mapseed = doc.Root.GetAttributeString("mapseed", "unknown");

            var saveFileFrame = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.6f), loadGameContainer.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, 0.1f)
            }, style: "InnerFrame")
            {
                UserData = "savefileframe"
            };

            new GUITextBlock(new RectTransform(new Vector2(1, 0.2f), saveFileFrame.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.05f)
            }, 
            Path.GetFileNameWithoutExtension(fileName), font: GUI.LargeFont, textAlignment: Alignment.Center);

            var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), saveFileFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            });

            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("Submarine")} : {subName}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("LastSaved")} : {saveTime}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("MapSeed")} : {mapseed}", font: GUI.SmallFont);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.15f), saveFileFrame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("Delete"), style: "GUIButtonSmall")
            {
                UserData = fileName,
                OnClicked = DeleteSave
            };

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;
            if (obj == null) { return false; }

            SaveUtil.DeleteSave(saveFile);
            prevSaveFiles?.Remove(saveFile);
            UpdateLoadMenu(prevSaveFiles);

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in loadGameContainer.Children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }

    }
}
