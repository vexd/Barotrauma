using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

//Client

namespace Barotrauma.Networking
{
    public partial class CustomSettings
    {
        private GUIComponent settingsFrame;

        const int MaxStartingCash = 50000;
        const int MinStartingCash = 2000;

        public GUIComponent SettingsFrame
        {
            get { return settingsFrame; }
        }

        internal void CreateSettingsFrame(ServerSettings serverSettings, GUIComponent parent)
        {
           // settingsFrame = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center));

            ServerSettings.CreateLabeledSlider(parent, "ServerSettingsCampaignStartingCash", out GUIScrollBar slider, out GUITextBlock sliderLabel);
            string CampaignStartingCashLabel = sliderLabel.Text + " ";
            slider.Step = 0.01f;
            slider.Range = new Vector2(MinStartingCash, MaxStartingCash);
            slider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                ((GUITextBlock)scrollBar.UserData).Text = CampaignStartingCashLabel + ((int)scrollBar.BarScrollValue).ToString();
                return true;
            };

            serverSettings.GetPropertyData("CampaignStartingCash").AssignGUIComponent(slider);
            slider.OnMoved(slider, slider.BarScroll);
        }

        public void ClientAdminRead(IReadMessage incMsg)
        {
        }

        public void ClientAdminWrite(IWriteMessage outMsg)
        {
        }
    }
}
