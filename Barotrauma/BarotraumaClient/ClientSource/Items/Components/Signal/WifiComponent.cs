using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent : IDrawableComponent
    {
        public override bool ShouldDrawHUD(Character character)
        {
            return IsShowingConfigurationUI && 
                character == Character.Controlled && 
                item.ParentInventory!=null && 
                item.ParentInventory.Owner == character;
        }

        public override void ShowConfigurationUI() 
        { 
            IsShowingConfigurationUI = true; 
            IsActive = true; 
            isShowingConfigScreen = true; 
            RecreateGUI(); 
        }

        public Vector2 DrawSize
        {
            get { return new Vector2(range * 2); }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (!editing || !MapEntity.SelectedList.Contains(item)) return;

            Vector2 pos = new Vector2(item.DrawPosition.X, -item.DrawPosition.Y);
            ShapeExtensions.DrawLine(spriteBatch, pos + Vector2.UnitY * range, pos - Vector2.UnitY * range, Color.Cyan * 0.5f, 2);
            ShapeExtensions.DrawLine(spriteBatch, pos + Vector2.UnitX * range, pos - Vector2.UnitX * range, Color.Cyan * 0.5f, 2);
            ShapeExtensions.DrawCircle(spriteBatch, pos, range, 32, Color.Cyan * 0.5f, 3);
        }

        private void RecreateGUI()
        {
            GuiFrame.ClearChildren();
            activeButtons.Clear();
            ChannelGroupConfigScreen.ClearChildren();
            CreateGUI();
        }

        partial void InitProjSpecific(XElement element)
        {
            if (GuiFrame != null)
                ChannelGroupConfigScreen = new GUIFrame(new RectTransform(GuiFrame.RectTransform.RelativeSize, GUI.Canvas, Anchor.Center), GuiFrame.Style.Name, GuiFrame.Color);

            CreateGUI();
            GameMain.Instance.OnResolutionChanged += RecreateGUI;
        }

        partial void UpdateProjSpecific()
        {
            //Check if conditions have changed
            bool shouldDraw = ShouldDrawHUD(Character.Controlled);
            IsShowingConfigurationUI = IsShowingConfigurationUI ? shouldDraw : false;
            isShowingConfigScreen = isShowingConfigScreen ? shouldDraw : false;
            isShowingSubConfigScreen = isShowingSubConfigScreen ? shouldDraw : false;

            //Set activity flags
            if (isShowingConfigScreen || isShowingSubConfigScreen)
                IsActive = true;

            //Hide or Show UI
            if (isShowingConfigScreen)
            {
                GuiFrame?.AddToGUIUpdateList();
            }
            else
            {
                GuiFrame?.RemoveFromGUIUpdateList();
            }

            if (isShowingSubConfigScreen)
            {
                ChannelGroupConfigScreen?.AddToGUIUpdateList();
            }
            else
            {
                ChannelGroupConfigScreen?.RemoveFromGUIUpdateList();
            }
        }

        private GUILayoutGroup uiElementContainer;
        private List<GUITickBox> activeButtons = new List<GUITickBox>();
        private GUIFrame ChannelGroupConfigScreen = null;
        private bool isShowingConfigScreen = false;
        private bool isShowingSubConfigScreen = false;

        private void ShowConfigScreen(ChannelGroup group)
        {
            if (ChannelGroupConfigScreen == null)
                return;

            uiElementContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.90f, 0.90f), ChannelGroupConfigScreen.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            {
                var titleLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), uiElementContainer.RectTransform)
                {
                    RelativeOffset = new Vector2(0.0f, 0.05f)
                }, 
                    isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f,
                };

                var titletext = new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), titleLayout.RectTransform), "Group Configuration");
                titleLayout.RectTransform.MinSize = new Point(0, titletext.RectTransform.MinSize.Y);
            }

            {
                //Edit Name
                var nameArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), uiElementContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), nameArea.RectTransform), TextManager.Get("GroupName"));
                var nameBox = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), nameArea.RectTransform), group.Name);
                nameBox.OnTextChanged += (textBox, text) =>
                {
                    group.Name = text;
                    item.CreateClientEvent(this);
                    return true;
                };

                nameArea.RectTransform.MinSize = new Point(0, nameBox.RectTransform.MinSize.Y);
            }

            {
                var headingsLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), uiElementContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = false,
                    RelativeSpacing = 0.02f,
                    ChildAnchor = Anchor.CenterLeft,
                };
                var channelText = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), headingsLayout.RectTransform), "Channel");
                var sendText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), headingsLayout.RectTransform), "Send");
                var recvText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), headingsLayout.RectTransform), "Recv");
            }

            {
                var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.6f), uiElementContainer.RectTransform));
                listBox.Padding *= 2.0f;
                listBox.Spacing = (int)(4 * GUI.Scale);

                var activeChannelGroup = ActiveChannelGroup;
                foreach (var kvp in group.Channels)
                {
                    var channelSetting = kvp.Value;

                    //Add group
                    var listEntryBg = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f),
                        listBox.Content.RectTransform, Anchor.TopCenter))
                    {
                        CanBeFocused = false,
                        UserData = channelSetting,
                    };

                    var channelGroupLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), listEntryBg.RectTransform, Anchor.Center)
                    {
                        RelativeOffset = new Vector2(0.05f, 0.0f)
                    }
                    , isHorizontal: true)
                    {
                        Stretch = false,
                        RelativeSpacing = 0.02f,
                        ChildAnchor = Anchor.CenterLeft,
                    };

                    var textblock = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), channelGroupLayout.RectTransform), channelSetting.ChannelId.ToString());

                    var send = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.5f), channelGroupLayout.RectTransform), "")
                    {
                        UserData = channelSetting,
                        Selected = channelSetting.Send,
                        OnSelected = (box) =>
                        {
                            channelSetting.Send = box.Selected;
                            item.CreateClientEvent(this);
                            return true;
                        }
                    };
                    var recv = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.5f), channelGroupLayout.RectTransform), "")
                    {
                        UserData = channelSetting,
                        Selected = channelSetting.Recieve,
                        OnSelected = (box) =>
                        {
                            channelSetting.Recieve = box.Selected;
                            item.CreateClientEvent(this);
                            return true;
                        }
                    };
                   
                    var removeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), channelGroupLayout.RectTransform),
                       TextManager.Get("Remove"), style: "GUIButtonSmall")
                    {
                        UserData = channelSetting,
                        OnClicked = (button, userData) =>
                        {
                            var channelGroup = group;
                            var channel = userData as ChannelSetting;
                            if (channelGroup !=null && channel != null)
                            {
                                channelGroup.RemoveChannel(channel);
                                RecreateConfigScreen(group);
                                item.CreateClientEvent(this);
                            }
                            return true;
                        }
                    };
                    channelGroupLayout.RectTransform.MinSize = new Point(0, send.Box.RectTransform.MinSize.Y);
                }
            }

            {
                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), uiElementContainer.RectTransform), isHorizontal: true)
                {
                    RelativeSpacing = 0.05f,
                    Stretch = true,
                };
                var addButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), TextManager.Get("Add"))
                {
                    UserData = group,
                    OnClicked = UIAddNewChannelToGroup,
                };
                var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), TextManager.Get("Close"))
                {
                    OnClicked = CloseChannelConfiguration
                };
                buttonContainer.RectTransform.MinSize = new Point(0, addButton.RectTransform.MinSize.Y);
            }

            isShowingSubConfigScreen = true;
        }

        private void RecreateConfigScreen(ChannelGroup group)
        {
            ChannelGroupConfigScreen?.ClearChildren();
            ShowConfigScreen(group);
        }

        private void CreateGUI()
        {
            if (GuiFrame == null)
                return;

            uiElementContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.90f, 0.90f), GuiFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            {
                var titleLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), uiElementContainer.RectTransform, Anchor.TopLeft)
                {
                    RelativeOffset = new Vector2(0.05f, 0.05f)
                }
                   , isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f,
                    ChildAnchor = Anchor.Center,
                };

                var titletext = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), titleLayout.RectTransform), "Radio Configuration");
                titleLayout.RectTransform.MinSize = new Point(0, titletext.RectTransform.MinSize.Y);
            }

            {
                var headingsLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), uiElementContainer.RectTransform, Anchor.Center)
                {
                    RelativeOffset = new Vector2(0.05f, 0.0f)
                }
                   , isHorizontal: true)
                {
                    Stretch = false,
                    RelativeSpacing = 0.02f,
                    ChildAnchor = Anchor.CenterLeft,
                };
                var activeText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), headingsLayout.RectTransform), "Active");
                var channelText = new GUITextBlock(new RectTransform(new Vector2(0.1f, 1.0f), headingsLayout.RectTransform), "Name");
            }

            {
                var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.6f), uiElementContainer.RectTransform));
                listBox.Padding *= 2.0f;
                listBox.Spacing = (int)(4 * GUI.Scale);

                var activeChannelGroup = ActiveChannelGroup;
                foreach (var channelGroup in ChannelGroups)
                {
                    //Add group
                    var listEntryBg = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f),
                        listBox.Content.RectTransform, Anchor.TopCenter))
                    {
                        CanBeFocused = false,
                        UserData = channelGroup,
                    };

                    var channelGroupLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), listEntryBg.RectTransform, Anchor.Center)
                    {
                        RelativeOffset = new Vector2(0.05f, 0.0f)
                    }
                    , isHorizontal: true)
                    {
                        Stretch = false,
                        RelativeSpacing = 0.02f,
                        ChildAnchor = Anchor.CenterLeft,
                    };

                    var active = new GUITickBox(new RectTransform(new Vector2(0.1f, 1.0f), channelGroupLayout.RectTransform), "")
                    {
                        UserData = channelGroup
                    };
                    if (activeChannelGroup == channelGroup)
                        active.Selected = true;
                    active.OnSelected = UIChannelGroupActivated;
                    activeButtons.Add(active);

                    var textblock = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), channelGroupLayout.RectTransform), channelGroup.Name);
                    var removeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), channelGroupLayout.RectTransform),
                       TextManager.Get("Remove"), style: "GUIButtonSmall")
                    {
                        UserData = channelGroup,
                        OnClicked = UIRemoveChannelGroup
                    };

                    var configButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), channelGroupLayout.RectTransform),
                      TextManager.Get("Config"), style: "GUIButtonSmall")
                    {
                        UserData = channelGroup,
                        OnClicked = UIShowChannelGroupConfig
                    };

                    channelGroupLayout.RectTransform.MinSize = new Point(0, active.Box.RectTransform.MinSize.Y);
                }
            }


            {
                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), uiElementContainer.RectTransform), isHorizontal: true)
                {
                    RelativeSpacing = 0.05f,
                    Stretch = true,
                };
                var addButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), TextManager.Get("Add"))
                {
                    OnClicked = UIAddNewChannelGroup
                };
                var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), TextManager.Get("Close"))
                {
                    OnClicked = CloseConfiguration
                };
                buttonContainer.RectTransform.MinSize = new Point(0, addButton.RectTransform.MinSize.Y);
            }
        }

        private bool UIAddNewChannelToGroup(GUIButton button, object obj)
        {
            ChannelGroup cg = button.UserData as ChannelGroup;
            if (cg != null)
            {
                cg.AddChannel();
                RecreateConfigScreen(cg);
            }
            return true;
        }

        private bool CloseChannelConfiguration(GUIButton button, object obj)
        {
            isShowingSubConfigScreen = false;

            //Roll back to the channel group UI
            if (ShouldDrawHUD(Character.Controlled))
            {
                isShowingConfigScreen = true;
                RecreateGUI();
            }

            return true;
        }

        private bool UIShowChannelGroupConfig(GUIButton button, object obj)
        {
            ChannelGroupConfigScreen.ClearChildren();
            ChannelGroup cg = button.UserData as ChannelGroup;
            if (cg != null)
            {
                ShowConfigScreen(cg);
                isShowingConfigScreen = false;
            }

            return true;
        }

        private bool UIChannelGroupActivated(GUITickBox box)
        {
            if (box.Selected)
            {
                ChannelGroup cg = box.UserData as ChannelGroup;
                ActiveChannelGroup = cg;

                foreach (var tickbox in activeButtons)
                {
                    if (tickbox != box)
                        tickbox.Selected = false;
                }
            }
            return true;
        }

        private bool UIRemoveChannelGroup(GUIButton button, object obj)
        {
            ChannelGroup removeTarget = button.UserData as ChannelGroup;
            if (removeTarget != null)
            {
                RemoveChanelGroup(removeTarget);
                if (activeChannelGroup == removeTarget)
                    activeChannelGroup = null;
                RecreateGUI();
            }

            return true;
        }
        private bool UIAddNewChannelGroup(GUIButton button, object obj)
        {
            AddChannelGroup();
            RecreateGUI();
            return true;
        }

        private bool CloseConfiguration(GUIButton button, object obj)
        {
            IsShowingConfigurationUI = false;

            return true;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            SharedWriteChannelGroups(msg);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            SharedReadChannelGroups(msg);
        }
    }
}
