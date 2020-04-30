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

            if (settingsFrame != null)
            {
                settingsFrame.Parent.ClearChildren();
                settingsFrame = null;
            }

            settingsFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), parent.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // Gameplay
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), settingsFrame.RectTransform), TextManager.Get("ServerSettingsCustomGameplay"), font: GUI.SubHeadingFont);

            var gameplaySettingsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.16f), settingsFrame.RectTransform))
            {
                AutoHideScrollBar = true,
                UseGridLayout = false
            };
            gameplaySettingsList.Padding *= 2.0f;

            var ForceAllowJobPrefs = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsList.Content.RectTransform), TextManager.Get("ServerSettingsForceJobPrefs"));
            serverSettings.GetPropertyData("ForceAllowPreferredJobs").AssignGUIComponent(ForceAllowJobPrefs);

            //Campaign
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), settingsFrame.RectTransform), TextManager.Get("ServerSettingsCustomCampaign"), font: GUI.SubHeadingFont);

            var campaignSettingsList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.16f), settingsFrame.RectTransform))
            {
                AutoHideScrollBar = true,
                UseGridLayout = false
            };
            gameplaySettingsList.Padding *= 2.0f;

            var allowCampaignRespawn = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), gameplaySettingsList.Content.RectTransform), TextManager.Get("ServerSettingsAllowCampaignRespawn"));
            serverSettings.GetPropertyData("AllowCampaignRespawn").AssignGUIComponent(allowCampaignRespawn);

            ServerSettings.CreateLabeledSlider(campaignSettingsList.Content, "ServerSettingsCampaignStartingCash", out GUIScrollBar slider, out GUITextBlock sliderLabel);
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
