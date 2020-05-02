using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
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

        public override void ShowConfigurationUI() { IsShowingConfigurationUI = true; IsActive = true; }
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
            CreateGUI();
        }

        partial void InitProjSpecific(XElement element)
        {
            CreateGUI();
            GameMain.Instance.OnResolutionChanged += RecreateGUI;
        }

        partial void UpdateProjSpecific()
        {
            if (IsShowingConfigurationUI)
            {
                IsShowingConfigurationUI = ShouldDrawHUD(Character.Controlled);
            }

            if (IsShowingConfigurationUI)
            {
                IsActive = true;
                AddToGUIUpdateList();
            }
            else
            {
                GuiFrame?.RemoveFromGUIUpdateList();
                //IsActive falls back to text prompt timer here
            }
        }

        private GUILayoutGroup uiElementContainer;

        private void CreateGUI()
        {
            if (GuiFrame == null)
                return;

            uiElementContainer = new GUILayoutGroup(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            },
               childAnchor: Anchor.Center)
            {
                RelativeSpacing = 0.05f,
                Stretch =false,
            };

            var btn = new GUIButton(
                    new RectTransform(
                        new Vector2(1.0f, 0.5f),
                        uiElementContainer.RectTransform),
                        TextManager.Get("ConfigButton"))
            {
            };

            //"Close"
            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.05f), uiElementContainer.RectTransform), style: null);
            var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform, Anchor.BottomRight), TextManager.Get("Close"))
            {
                OnClicked = CloseConfiguration
            };
        }

        private bool CloseConfiguration(GUIButton button, object obj)
        {
            IsShowingConfigurationUI = false;

            return true;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            MultiChannelConfig.WriteMultiChannelConfigMsg(msg);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            // Read channel updates from server - in order for clients to sync send/recieve settings
            // All checks are still done on the server
            MultiChannelConfig.Clear();
            MultiChannelConfig.ReadMultiChannelConfigMsg(msg);
        }
    }
}
