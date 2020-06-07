﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Sonar : Powered, IServerSerializable, IClientSerializable
    {
        public enum BlipType
        {
            Default,
            Disruption
        }

        private PathFinder pathFinder;

        private bool dynamicDockingIndicator = true;

        private bool unsentChanges;
        private float networkUpdateTimer;

        public GUIButton SonarModeSwitch { get; private set; }
        private GUITickBox activeTickBox, passiveTickBox;
        private GUITextBlock signalWarningText;

        private GUIScrollBar zoomSlider;

        private GUIButton directionalModeSwitch;
        private Vector2? pingDragDirection = null;

        private GUIFrame controlContainer;

        private GUICustomComponent sonarView;

        private Sprite directionalPingBackground;
        private Sprite[] directionalPingButton;

        private Sprite pingCircle, directionalPingCircle;
        private Sprite screenOverlay, screenBackground;

        private Sprite sonarBlip;
        private Sprite lineSprite;

        private readonly Dictionary<string, Sprite> targetIcons = new Dictionary<string, Sprite>();

        private float displayBorderSize;

        private List<SonarBlip> sonarBlips;

        private float prevPassivePingRadius;

        private Vector2 center;
        private float displayScale;

        private const float DisruptionUpdateInterval = 0.2f;
        private float disruptionUpdateTimer;

        private float zoomSqrt;

        private float showDirectionalIndicatorTimer;

        //Vector2 = vector from the ping source to the position of the disruption
        //float = strength of the disruption, between 0-1
        private readonly List<Pair<Vector2, float>> disruptedDirections = new List<Pair<Vector2, float>>();

        class CachedDistance
        {
            public readonly Vector2 TransducerWorldPos;
            public readonly Vector2 WorldPos;
            public readonly float Distance;
            public double RecalculationTime;

            public CachedDistance(Vector2 transducerWorldPos, Vector2 worldPos, float dist)
            {
                TransducerWorldPos = transducerWorldPos;
                WorldPos = worldPos;
                Distance = dist;
            }
        }

        private readonly Dictionary<object, CachedDistance> markerDistances = new Dictionary<object, CachedDistance>();

        private readonly Color positiveColor = Color.Green;
        private readonly Color warningColor = Color.Orange;
        private readonly Color negativeColor = Color.Red;
        private readonly Color markerColor = Color.Red;

        public static readonly Vector2 controlBoxSize = new Vector2(0.33f, 0.32f);
        public static readonly Vector2 controlBoxOffset = new Vector2(0.025f, 0);
        private static readonly float sonarAreaSize = 1.09f;

        private static readonly Dictionary<BlipType, Color[]> blipColorGradient = new Dictionary<BlipType, Color[]>()
        {
            {
                BlipType.Default,
                new Color[] { Color.TransparentBlack, new Color(0, 50, 160), new Color(0, 133, 166), new Color(2, 159, 30), new Color(255, 255, 255) }
            },
            {
                BlipType.Disruption,
                new Color[] { Color.TransparentBlack, new Color(254, 68, 19), new Color(255, 220, 62), new Color(255, 255, 255) }
            }
        };

        private float prevDockingDist;

        public Vector2 DisplayOffset { get; private set; }

        public float DisplayRadius { get; private set; }

        private bool DisplayAllRuins = true;
        private bool DisplayAllWrecks = true;

        public static Vector2 GUISizeCalculation => Vector2.One * Math.Min(GUI.RelativeHorizontalAspectRatio, 1f) * sonarAreaSize;

        partial void InitProjSpecific(XElement element)
        {
            System.Diagnostics.Debug.Assert(Enum.GetValues(typeof(BlipType)).Cast<BlipType>().All(t => blipColorGradient.ContainsKey(t)));
            sonarBlips = new List<SonarBlip>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pingcircle":
                        pingCircle = new Sprite(subElement);
                        break;
                    case "directionalpingcircle":
                        directionalPingCircle = new Sprite(subElement);
                        break;
                    case "directionalpingbackground":
                        directionalPingBackground = new Sprite(subElement);
                        break;
                    case "directionalpingbutton":
                        if (directionalPingButton == null) { directionalPingButton = new Sprite[3]; }
                        int index = subElement.GetAttributeInt("index", 0);
                        directionalPingButton[index] = new Sprite(subElement);
                        break;
                    case "screenoverlay":
                        screenOverlay = new Sprite(subElement);
                        break;
                    case "screenbackground":
                        screenBackground = new Sprite(subElement);
                        break;
                    case "blip":
                        sonarBlip = new Sprite(subElement);
                        break;
                    case "linesprite":
                        lineSprite = new Sprite(subElement);
                        break;
                    case "icon":
                        var targetIconSprite = new Sprite(subElement);
                        targetIcons.Add(subElement.GetAttributeString("identifier", ""), targetIconSprite);
                        break;
                }
            }
            CreateGUI();
            GameMain.Instance.OnResolutionChanged += RecreateGUI;
        }

        private void RecreateGUI()
        {
            GuiFrame.ClearChildren();
            CreateGUI();
        }

        private void CreateGUI()
        {
            bool isConnectedToSteering = item.GetComponent<Steering>() != null;
            Vector2 size = isConnectedToSteering ? controlBoxSize : new Vector2(controlBoxSize.X * 2.0f, controlBoxSize.Y);

            controlContainer = new GUIFrame(new RectTransform(size, GuiFrame.RectTransform, Anchor.BottomRight, Pivot.BottomLeft), "ItemUI");
            var paddedControlContainer = new GUIFrame(new RectTransform(controlContainer.Rect.Size - GUIStyle.ItemFrameMargin, controlContainer.RectTransform, Anchor.Center)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            }, style: null);
            // Based on the height difference to the steering control box so that the elements keep the same size
            float extraHeight = 0.03f;
            var sonarModeArea = new GUIFrame(new RectTransform(new Vector2(1, 0.4f + extraHeight), paddedControlContainer.RectTransform, Anchor.TopCenter), style: null);
            SonarModeSwitch = new GUIButton(new RectTransform(new Vector2(0.2f, 1), sonarModeArea.RectTransform), string.Empty, style: "SwitchVertical")
            {
                Selected = false,
                Enabled = true,
                OnClicked = (button, data) =>
                {
                    button.Selected = !button.Selected;
                    CurrentMode = button.Selected ? Mode.Active : Mode.Passive;
                    if (GameMain.Client != null)
                    {
                        unsentChanges = true;
                        correctionTimer = CorrectionDelay;
                    }
                    return true;
                }
            };
            var sonarModeRightSide = new GUIFrame(new RectTransform(new Vector2(0.7f, 0.8f), sonarModeArea.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(SonarModeSwitch.RectTransform.RelativeSize.X, 0)
            }, style: null);
            passiveTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), sonarModeRightSide.RectTransform, Anchor.TopLeft),
                TextManager.Get("SonarPassive"), font: GUI.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                ToolTip = TextManager.Get("SonarTipPassive"),
                Selected = true,
                Enabled = false
            };
            activeTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), sonarModeRightSide.RectTransform, Anchor.BottomLeft),
                TextManager.Get("SonarActive"), font: GUI.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                ToolTip = TextManager.Get("SonarTipActive"),
                Selected = false,
                Enabled = false
            };
            passiveTickBox.TextBlock.OverrideTextColor(GUI.Style.TextColor);
            activeTickBox.TextBlock.OverrideTextColor(GUI.Style.TextColor);

            var lowerArea = new GUIFrame(new RectTransform(new Vector2(1, 0.4f + extraHeight), paddedControlContainer.RectTransform, Anchor.BottomCenter), style: null);
            var zoomContainer = new GUIFrame(new RectTransform(new Vector2(1, 0.45f), lowerArea.RectTransform, Anchor.TopCenter), style: null);
            var zoomText = new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.6f), zoomContainer.RectTransform, Anchor.CenterLeft),
                TextManager.Get("SonarZoom"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterRight);
            zoomSlider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 0.8f), zoomContainer.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(0.35f, 0)
            }, barSize: 0.15f, isHorizontal: true, style: "DeviceSlider")
            {
                OnMoved = (scrollbar, scroll) =>
                {
                    zoom = MathHelper.Lerp(MinZoom, MaxZoom, scroll);
                    if (GameMain.Client != null)
                    {
                        unsentChanges = true;
                        correctionTimer = CorrectionDelay;
                    }
                    return true;
                }
            };

            new GUIFrame(new RectTransform(new Vector2(0.8f, 0.01f), paddedControlContainer.RectTransform, Anchor.Center), style: "HorizontalLine");

            var directionalModeFrame = new GUIFrame(new RectTransform(new Vector2(1, 0.45f), lowerArea.RectTransform, Anchor.BottomCenter), style: null);
            directionalModeSwitch = new GUIButton(new RectTransform(new Vector2(0.3f, 0.8f), directionalModeFrame.RectTransform, Anchor.CenterLeft), string.Empty, style: "SwitchHorizontal")
            {
                OnClicked = (button, data) =>
                {
                    useDirectionalPing = !useDirectionalPing;
                    button.Selected = useDirectionalPing;
                    if (GameMain.Client != null)
                    {
                        unsentChanges = true;
                        correctionTimer = CorrectionDelay;
                    }
                    return true;
                }
            };
            var directionalModeSwitchText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1), directionalModeFrame.RectTransform, Anchor.CenterRight),
                TextManager.Get("SonarDirectionalPing"), GUI.Style.TextColor, GUI.SubHeadingFont, Alignment.CenterLeft);


            GuiFrame.CanBeFocused = false;

            GUITextBlock.AutoScaleAndNormalize(passiveTickBox.TextBlock, activeTickBox.TextBlock, zoomText, directionalModeSwitchText);

            sonarView = new GUICustomComponent(new RectTransform(Vector2.One * 0.7f, GuiFrame.RectTransform, Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight),
                (spriteBatch, guiCustomComponent) => { DrawSonar(spriteBatch, guiCustomComponent.Rect); }, null);

            signalWarningText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), sonarView.RectTransform, Anchor.Center, Pivot.BottomCenter),
                "", warningColor, GUI.LargeFont, Alignment.Center);

            // Setup layout for nav terminal
            if (isConnectedToSteering)
            {
                controlContainer.RectTransform.RelativeOffset = controlBoxOffset;
                controlContainer.RectTransform.SetPosition(Anchor.TopLeft);
                sonarView.RectTransform.ScaleBasis = ScaleBasis.Smallest;
                sonarView.RectTransform.SetPosition(Anchor.CenterRight);
                sonarView.RectTransform.Resize(GUISizeCalculation);
                GUITextBlock.AutoScaleAndNormalize(passiveTickBox.TextBlock, activeTickBox.TextBlock, zoomText, directionalModeSwitchText);
            }
        }

        private void SetPingDirection(Vector2 direction)
        {
            pingDirection = direction;
            if (GameMain.Client != null)
            {
                unsentChanges = true;
                correctionTimer = CorrectionDelay;
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            zoomSlider.BarScroll = MathUtils.InverseLerp(MinZoom, MaxZoom, zoom);
            //make the sonarView customcomponent render the steering view so it gets drawn in front of the sonar
            item.GetComponent<Steering>()?.AttachToSonarHUD(sonarView);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            showDirectionalIndicatorTimer -= deltaTime;
            if (GameMain.Client != null)
            {
                if (unsentChanges)
                {
                    if (networkUpdateTimer <= 0.0f)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                        networkUpdateTimer = 0.1f;
                        unsentChanges = false;
                    }
                }
                networkUpdateTimer -= deltaTime;
            }

            if (sonarView.Rect.Contains(PlayerInput.MousePosition))
            {
                float scrollSpeed = PlayerInput.ScrollWheelSpeed / 1000.0f;
                if (Math.Abs(scrollSpeed) > 0.0001f)
                {
                    zoomSlider.BarScroll += PlayerInput.ScrollWheelSpeed / 1000.0f;
                    zoomSlider.OnMoved(zoomSlider, zoomSlider.BarScroll);
                }
            }
            
            float distort = 1.0f - item.Condition / item.MaxCondition;
            for (int i = sonarBlips.Count - 1; i >= 0; i--)
            {
                sonarBlips[i].FadeTimer -= deltaTime * MathHelper.Lerp(0.5f, 2.0f, distort);
                sonarBlips[i].Position += sonarBlips[i].Velocity * deltaTime;

                if (sonarBlips[i].FadeTimer <= 0.0f) { sonarBlips.RemoveAt(i); }
            }

            //sonar view can only get focus when the cursor is inside the circle
            sonarView.CanBeFocused = 
                Vector2.DistanceSquared(sonarView.Rect.Center.ToVector2(), PlayerInput.MousePosition) <
                (sonarView.Rect.Width / 2 * sonarView.Rect.Width / 2);

            if (UseTransducers && connectedTransducers.Count == 0)
            {
                return;
            }

            Vector2 transducerCenter = GetTransducerPos() + DisplayOffset;

            if (Level.Loaded != null)
            {
                Dictionary<LevelTrigger, Vector2> levelTriggerFlows = new Dictionary<LevelTrigger, Vector2>();
                for (var pingIndex = 0; pingIndex < activePingsCount; ++pingIndex)
                {
                    var activePing = activePings[pingIndex];
                    foreach (LevelObject levelObject in Level.Loaded.LevelObjectManager.GetAllObjects(transducerCenter, range * activePing.State / zoom))
                    {
                        //gather all nearby triggers that are causing the water to flow into the dictionary
                        foreach (LevelTrigger trigger in levelObject.Triggers)
                        {
                            Vector2 flow = trigger.GetWaterFlowVelocity();
                            //ignore ones that are barely doing anything (flow^2 < 1)
                            if (flow.LengthSquared() > 1.0f && !levelTriggerFlows.ContainsKey(trigger))
                            {
                                levelTriggerFlows.Add(trigger, flow);
                            }
                        }
                    }
                }

                foreach (KeyValuePair<LevelTrigger, Vector2> triggerFlow in levelTriggerFlows)
                {
                    LevelTrigger trigger = triggerFlow.Key;
                    Vector2 flow = triggerFlow.Value;

                    float flowMagnitude = flow.Length();
                    if (Rand.Range(0.0f, 1.0f) < flowMagnitude / 1000.0f)
                    {
                        float edgeDist = Rand.Range(0.0f, 1.0f);
                        Vector2 blipPos = trigger.WorldPosition + Rand.Vector(trigger.ColliderRadius * edgeDist);
                        Vector2 blipVel = flow;
                        if (trigger.ForceFalloff) flow *= (1.0f - edgeDist);

                        //go through other triggers in range and add the flows of the ones that the blip is inside
                        foreach (KeyValuePair<LevelTrigger, Vector2> triggerFlow2 in levelTriggerFlows)
                        {
                            LevelTrigger trigger2 = triggerFlow2.Key;
                            if (trigger2 != trigger && Vector2.DistanceSquared(blipPos, trigger2.WorldPosition) < trigger2.ColliderRadius * trigger2.ColliderRadius)
                            {
                                Vector2 trigger2flow = triggerFlow2.Value;
                                if (trigger2.ForceFalloff) trigger2flow *= (1.0f - Vector2.Distance(blipPos, trigger2.WorldPosition) / trigger2.ColliderRadius);
                                blipVel += trigger2flow;
                            }
                        }
                        var flowBlip = new SonarBlip(blipPos, Rand.Range(0.5f, 1.0f), 1.0f)
                        {
                            Velocity = blipVel * Rand.Range(1.0f, 5.0f),
                            Size = new Vector2(MathHelper.Lerp(0.4f, 5f, flowMagnitude / 500.0f), 0.2f),
                            Rotation = (float)Math.Atan2(-blipVel.Y, blipVel.X)
                        };
                        sonarBlips.Add(flowBlip);
                    }
                }

                float outsideLevelFlow = 0.0f;
                if (transducerCenter.X < 0.0f)
                {
                    outsideLevelFlow = Math.Abs(transducerCenter.X * 0.001f);
                }
                else if (transducerCenter.X > Level.Loaded.Size.X)
                {
                    outsideLevelFlow = -(transducerCenter.X - Level.Loaded.Size.X) * 0.001f;
                }

                if (Rand.Range(0.0f, 100.0f) < Math.Abs(outsideLevelFlow))
                {
                    Vector2 blipPos = transducerCenter + Rand.Vector(Rand.Range(0.0f, range));
                    var flowBlip = new SonarBlip(blipPos, Rand.Range(0.5f, 1.0f), 1.0f)
                    {
                        Velocity = Vector2.UnitX * outsideLevelFlow * Rand.Range(50.0f, 100.0f),
                        Size = new Vector2(Rand.Range(0.4f, 5f), 0.2f),
                        Rotation = 0.0f
                    };
                    sonarBlips.Add(flowBlip);                    
                }
            }

            Steering steering = item.GetComponent<Steering>();
            if (steering != null && steering.DockingModeEnabled && steering.ActiveDockingSource != null)
            {
                float dockingDist = Vector2.Distance(steering.ActiveDockingSource.Item.WorldPosition, steering.DockingTarget.Item.WorldPosition);
                if (prevDockingDist > steering.DockingAssistThreshold && dockingDist <= steering.DockingAssistThreshold)
                {
                    zoomSlider.BarScroll = 0.25f;
                    zoom = Math.Max(zoom, MathHelper.Lerp(MinZoom, MaxZoom, zoomSlider.BarScroll));
                }
                else if (prevDockingDist > steering.DockingAssistThreshold * 0.75f && dockingDist <= steering.DockingAssistThreshold * 0.75f)
                {
                    zoomSlider.BarScroll = 0.5f;
                    zoom = Math.Max(zoom, MathHelper.Lerp(MinZoom, MaxZoom, zoomSlider.BarScroll));
                }
                else if (prevDockingDist > steering.DockingAssistThreshold * 0.5f && dockingDist <= steering.DockingAssistThreshold * 0.5f)
                {
                    zoomSlider.BarScroll = 0.25f;
                    zoom = Math.Max(zoom, MathHelper.Lerp(MinZoom, MaxZoom, zoomSlider.BarScroll));
                }
                prevDockingDist = Math.Min(dockingDist, prevDockingDist);
            }
            else
            {
                prevDockingDist = float.MaxValue;
            }

            if (steering != null && directionalPingButton != null)
            {
                steering.SteerRadius = useDirectionalPing && pingDragDirection != null ?
                    -1.0f :
                    PlayerInput.PrimaryMouseButtonDown() || !PlayerInput.PrimaryMouseButtonHeld() ?
                        (float?)((sonarView.Rect.Width / 2) - (directionalPingButton[0].size.X * sonarView.Rect.Width / screenBackground.size.X)) :
                        null;                
            }

            if (useDirectionalPing)
            {
                Vector2 newDragDir = Vector2.Normalize(PlayerInput.MousePosition - sonarView.Rect.Center.ToVector2());
                if (MouseInDirectionalPingRing(sonarView.Rect, true) && PlayerInput.PrimaryMouseButtonDown())
                {
                    pingDragDirection = newDragDir;
                }

                if (pingDragDirection != null && PlayerInput.PrimaryMouseButtonHeld())
                {
                    float newAngle = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(newDragDir));
                    SetPingDirection(new Vector2((float)Math.Cos(newAngle), (float)Math.Sin(newAngle)));
                }
                else
                {
                    pingDragDirection = null;
                }
            }
            else
            {
                pingDragDirection = null;
            }
            
            disruptionUpdateTimer -= deltaTime;

            for (var pingIndex = 0; pingIndex < activePingsCount; ++pingIndex)
            {
                var activePing = activePings[pingIndex];
                float pingRadius = DisplayRadius * activePing.State / zoom;
                if (disruptionUpdateTimer <= 0.0f) { UpdateDisruptions(transducerCenter, pingRadius / displayScale); }               
                Ping(transducerCenter, transducerCenter,
                    pingRadius, activePing.PrevPingRadius, displayScale, range / zoom, passive: false, pingStrength: 2.0f);
                activePing.PrevPingRadius = pingRadius;
            }

            if (disruptionUpdateTimer <= 0.0f)
            {
                disruptionUpdateTimer = DisruptionUpdateInterval;
            }

            if (currentMode == Mode.Active && currentPingIndex != -1)
            {
                return;
            }

            float passivePingRadius = (float)(Timing.TotalTime % 1.0f);
            if (passivePingRadius > 0.0f)
            {
                disruptedDirections.Clear();
                foreach (AITarget t in AITarget.List)
                {
                    if (t.Entity is Character c && c.Params.HideInSonar) { continue; }
                    if (t.SoundRange <= 0.0f || float.IsNaN(t.SoundRange) || float.IsInfinity(t.SoundRange)) { continue; }
                    
                    float distSqr = Vector2.DistanceSquared(t.WorldPosition, transducerCenter);
                    if (distSqr > t.SoundRange * t.SoundRange * 2) { continue; }

                    float dist = (float)Math.Sqrt(distSqr);
                    if (dist > prevPassivePingRadius * Range && dist <= passivePingRadius * Range && Rand.Int(sonarBlips.Count) < 500)
                    {
                        Ping(t.WorldPosition, transducerCenter,
                            Math.Min(t.SoundRange, range * 0.5f) * displayScale, 0, displayScale, Math.Min(t.SoundRange, range * 0.5f), 
                            passive: true, pingStrength: 0.5f);
                        sonarBlips.Add(new SonarBlip(t.WorldPosition, 1.0f, 1.0f));
                    }
                }
            }
            prevPassivePingRadius = passivePingRadius;
        }
        
        private bool MouseInDirectionalPingRing(Rectangle rect, bool onButton)
        {
            if (!useDirectionalPing || directionalPingButton == null) { return false; }

            float endRadius = rect.Width / 2.0f;
            float startRadius = endRadius - directionalPingButton[0].size.X * rect.Width / screenBackground.size.X;

            Vector2 center = rect.Center.ToVector2();

            float dist = Vector2.DistanceSquared(PlayerInput.MousePosition,center);
            
            bool retVal = (dist >= startRadius*startRadius) && (dist < endRadius*endRadius);
            if (onButton)
            {
                float pingAngle = MathUtils.VectorToAngle(pingDirection);
                float mouseAngle = MathUtils.VectorToAngle(Vector2.Normalize(PlayerInput.MousePosition - center));
                retVal &= Math.Abs(MathUtils.GetShortestAngle(mouseAngle, pingAngle)) < MathHelper.ToRadians(DirectionalPingSector * 0.5f);
            }

            return retVal;
        }

        private void DrawSonar(SpriteBatch spriteBatch, Rectangle rect)
        {
            displayBorderSize = 0.2f;
            center = rect.Center.ToVector2();
            DisplayRadius = (rect.Width / 2.0f) * (1.0f - displayBorderSize);
            displayScale = DisplayRadius / range * zoom;

            if (screenBackground != null)
            {
                screenBackground.Draw(spriteBatch, center, 0.0f, rect.Width / screenBackground.size.X);
            }

            if (useDirectionalPing)
            {
                directionalPingBackground?.Draw(spriteBatch, center, 0.0f, rect.Width / directionalPingBackground.size.X);
                if (directionalPingButton != null)
                {
                    int buttonSprIndex = 0;
                    if (pingDragDirection != null)
                    {
                        buttonSprIndex = 2;
                    }
                    else if (MouseInDirectionalPingRing(rect, true))
                    {
                        buttonSprIndex = 1;
                    }
                    directionalPingButton[buttonSprIndex]?.Draw(spriteBatch, center, MathUtils.VectorToAngle(pingDirection), rect.Width / directionalPingBackground.size.X);
                }
            }

            if (currentMode == Mode.Active && currentPingIndex != -1)
            {
                var activePing = activePings[currentPingIndex];
                if (activePing.IsDirectional && directionalPingCircle != null)
                {
                    directionalPingCircle.Draw(spriteBatch, center, Color.White * (1.0f - activePing.State),
                    rotate: MathUtils.VectorToAngle(activePing.Direction),
                    scale: (DisplayRadius / directionalPingCircle.size.X) * activePing.State);
                }
                else
                {
                    pingCircle.Draw(spriteBatch, center, Color.White * (1.0f - activePing.State), 0.0f, (DisplayRadius * 2 / pingCircle.size.X) * activePing.State);
                }
            }

            float signalStrength = 1.0f;
            if (UseTransducers)
            {
                signalStrength = 0.0f;
                foreach (ConnectedTransducer connectedTransducer in connectedTransducers)
                {
                    signalStrength = Math.Max(signalStrength, connectedTransducer.SignalStrength);
                }
            }

            Vector2 transducerCenter = GetTransducerPos();


            if (sonarBlips.Count > 0)
            {
                zoomSqrt = (float)Math.Sqrt(zoom);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

                foreach (SonarBlip sonarBlip in sonarBlips)
                {
                    DrawBlip(spriteBatch, sonarBlip, transducerCenter, center, sonarBlip.FadeTimer / 2.0f * signalStrength);
                }

                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
            }

            if (item.Submarine != null && !DetectSubmarineWalls)
            {
                DrawDockingPorts(spriteBatch, transducerCenter, signalStrength);
                transducerCenter += DisplayOffset;
                DrawOwnSubmarineBorders(spriteBatch, transducerCenter, signalStrength);
            }
            else
            {
                DisplayOffset = Vector2.Zero;
            }

            float directionalPingVisibility = useDirectionalPing && currentMode == Mode.Active ? 1.0f : showDirectionalIndicatorTimer;
            if (directionalPingVisibility > 0.0f)
            {
                Vector2 sector1 = MathUtils.RotatePointAroundTarget(pingDirection * DisplayRadius, Vector2.Zero, DirectionalPingSector * 0.5f);
                Vector2 sector2 = MathUtils.RotatePointAroundTarget(pingDirection * DisplayRadius, Vector2.Zero, -DirectionalPingSector * 0.5f);
                DrawLine(spriteBatch, Vector2.Zero, sector1, Color.LightCyan * 0.2f * directionalPingVisibility, width: 3);
                DrawLine(spriteBatch, Vector2.Zero, sector2, Color.LightCyan * 0.2f * directionalPingVisibility, width: 3);
            }

            if (GameMain.DebugDraw)
            {
                GUI.DrawString(spriteBatch, rect.Location.ToVector2(), sonarBlips.Count.ToString(), Color.White);
            }

            if (screenOverlay != null)
            {
                screenOverlay.Draw(spriteBatch, center, 0.0f, rect.Width / screenOverlay.size.X);
            }

            if (signalStrength <= 0.5f)
            {
                signalWarningText.Text = TextManager.Get(signalStrength <= 0.0f ? "SonarNoSignal" : "SonarSignalWeak");
                signalWarningText.Color = signalStrength <= 0.0f ? negativeColor : warningColor;
                signalWarningText.Visible = true;
                return;
            }
            else
            {
                signalWarningText.Visible = false;
            }

            if (GameMain.GameSession == null) { return; }

            if (Level.Loaded == null) { return; }

            DrawMarker(spriteBatch,
                GameMain.GameSession.StartLocation.Name,
                "outpost",
                GameMain.GameSession.StartLocation.Name,
                Level.Loaded.StartPosition, transducerCenter,
                displayScale, center, DisplayRadius);

            DrawMarker(spriteBatch,
                GameMain.GameSession.EndLocation.Name,
                "outpost",
                GameMain.GameSession.EndLocation.Name,
                Level.Loaded.EndPosition, transducerCenter, 
                displayScale, center, DisplayRadius);

            foreach (AITarget aiTarget in AITarget.List)
            {
                if (!aiTarget.Enabled) continue;
                if (string.IsNullOrEmpty(aiTarget.SonarLabel) || aiTarget.SoundRange <= 0.0f) continue;

                if (Vector2.DistanceSquared(aiTarget.WorldPosition, transducerCenter) < aiTarget.SoundRange * aiTarget.SoundRange)
                {
                    DrawMarker(spriteBatch,
                        aiTarget.SonarLabel,
                        aiTarget.SonarIconIdentifier,
                        aiTarget,
                        aiTarget.WorldPosition, transducerCenter, 
                        displayScale, center, DisplayRadius * 0.975f);
                }
            }
            
            if (GameMain.GameSession.Mission != null)
            {
                var mission = GameMain.GameSession.Mission;

                if (!string.IsNullOrWhiteSpace(mission.SonarLabel))
                {
                    foreach (Vector2 sonarPosition in mission.SonarPositions)
                    {
                        DrawMarker(spriteBatch,
                            mission.SonarLabel,
                            mission.SonarIconIdentifier,
                            mission,
                            sonarPosition, transducerCenter, 
                            displayScale, center, DisplayRadius * 0.95f);
                    }
                }
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub==null || !sub.ShowSonarMarker) { continue; }
                if (UseTransducers ?
                    connectedTransducers.Any(t => sub == t.Transducer.Item.Submarine || sub.DockedTo.Contains(t.Transducer.Item.Submarine)) :
                    (sub == item.Submarine || sub.DockedTo.Contains(item.Submarine)))
                {
                    continue;
                }
                if (sub.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }
                             
                DrawMarker(spriteBatch, 
                    sub.Info.DisplayName, 
                    sub.Info.HasTag(SubmarineTag.Shuttle) ? "shuttle" : "submarine",
                    sub,
                    sub.WorldPosition, transducerCenter, 
                    displayScale, center, DisplayRadius * 0.95f);
            }


            uint nameIdx = 0;
            if (DisplayAllRuins)
            {
                
                foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                {
                    if (ruin == null)
                        continue;

                    Vector2 ruinsPos = ruin.Area.Center.ToVector2();
                    string ruinName = ArmyAlphabet.GetArmyAlphabetEntry(nameIdx);
                    ruinName = ruinName != null ? ruinName : nameIdx.ToString();
                    string ruinLabel = "Ruin " + ruinName;

                    DrawMarker(spriteBatch,
                          ruinLabel,
                          "", //TODO custom icon
                          ruin,
                          ruinsPos, transducerCenter,
                          displayScale, center, DisplayRadius * 0.95f);

                    nameIdx++;
                }
            }

            if (DisplayAllWrecks)
            {
                foreach (Submarine sub in Level.Loaded.Wrecks)
                {
                    if (sub == null)
                        continue;

                    Vector2 pos = sub.WorldPosition;
                    string name = ArmyAlphabet.GetArmyAlphabetEntry(nameIdx);
                    name = name != null ? name : nameIdx.ToString();
                    string label = sub.Info.DisplayName + " " + name;

                    DrawMarker(spriteBatch,
                      label,
                      sub.Info.HasTag(SubmarineTag.Shuttle) ? "shuttle" : "submarine",
                      sub,
                      sub.WorldPosition, transducerCenter,
                      displayScale, center, DisplayRadius * 0.95f);

                    nameIdx++;
                }
            }

            if (GameMain.DebugDraw)
            {
                var steering = item.GetComponent<Steering>();
                steering?.DebugDrawHUD(spriteBatch, transducerCenter, displayScale, DisplayRadius, center);
            }
        }

        private void DrawOwnSubmarineBorders(SpriteBatch spriteBatch, Vector2 transducerCenter, float signalStrength)
        {
            float simScale = displayScale * Physics.DisplayToSimRation * zoom;

            foreach (Submarine submarine in Submarine.Loaded)
            {
                if (UseTransducers ?
                    !connectedTransducers.Any(t => submarine == t.Transducer.Item.Submarine || submarine.DockedTo.Contains(t.Transducer.Item.Submarine)) :
                    submarine != item.Submarine && !submarine.DockedTo.Contains(item.Submarine)) continue;
                if (submarine.HullVertices == null) continue;

                Vector2 offset = ConvertUnits.ToSimUnits(submarine.WorldPosition - transducerCenter);

                for (int i = 0; i < submarine.HullVertices.Count; i++)
                {
                    Vector2 start = (submarine.HullVertices[i] + offset) * simScale;
                    start.Y = -start.Y;
                    Vector2 end = (submarine.HullVertices[(i + 1) % submarine.HullVertices.Count] + offset) * simScale;
                    end.Y = -end.Y;

                    DrawLine(spriteBatch, start, end, Color.LightBlue * signalStrength * 0.5f, width: 4);
                }
            }
        }

        private void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int width)
        {
            bool startOutside = start.LengthSquared() > DisplayRadius * DisplayRadius;
            bool endOutside = end.LengthSquared() > DisplayRadius * DisplayRadius;
            if (startOutside && endOutside)
            {
                return;
            }
            else if (startOutside)
            {
                if (MathUtils.GetLineCircleIntersections(Vector2.Zero, DisplayRadius, end, start, true, out Vector2? intersection1, out _) == 1)
                {
                    DrawLineSprite(spriteBatch, center + intersection1.Value, center + end, color, width: width);
                }
            }
            else if (endOutside)
            {
                if (MathUtils.GetLineCircleIntersections(Vector2.Zero, DisplayRadius, start, end, true, out Vector2? intersection1, out _) == 1)
                {
                    DrawLineSprite(spriteBatch, center + start, center + intersection1.Value, color, width: width);
                }
            }
            else
            {
                DrawLineSprite(spriteBatch, center + start, center + end, color, width: width);
            }
        }

        private void DrawLineSprite(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int width)
        {
            if (lineSprite == null)
            {
                GUI.DrawLine(spriteBatch, start, end, color, width: width);
            }
            else
            {
                Vector2 dir = end - start;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                lineSprite.Draw(spriteBatch, start, color, origin: lineSprite.Origin, rotate: angle,
                    scale: new Vector2(dir.Length() / lineSprite.size.X, 1.0f));
            }
        }


        private void DrawDockingPorts(SpriteBatch spriteBatch, Vector2 transducerCenter, float signalStrength)
        {
            float scale = displayScale * zoom;

            Steering steering = item.GetComponent<Steering>();
            if (steering != null && steering.DockingModeEnabled && steering.ActiveDockingSource != null)
            {
                DrawDockingIndicator(spriteBatch, steering, ref transducerCenter);
            }
            else
            {
                DisplayOffset = Vector2.Lerp(DisplayOffset, Vector2.Zero, 0.1f);
            }                

            foreach (DockingPort dockingPort in DockingPort.List)
            {
                if (Level.Loaded != null && dockingPort.Item.Submarine.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }

                if (dockingPort.Item.Submarine == null) { continue; }
                if (dockingPort.Item.Submarine.Info.IsWreck) { continue; }

                //don't show the docking ports of the opposing team on the sonar
                if (item.Submarine != null)
                {
                    if ((dockingPort.Item.Submarine.TeamID == Character.TeamType.Team1 && item.Submarine.TeamID == Character.TeamType.Team2) ||
                        (dockingPort.Item.Submarine.TeamID == Character.TeamType.Team2 && item.Submarine.TeamID == Character.TeamType.Team1))
                    {
                        continue;
                    }
                }

                Vector2 offset = (dockingPort.Item.WorldPosition - transducerCenter) * scale;
                offset.Y = -offset.Y;
                if (offset.LengthSquared() > DisplayRadius * DisplayRadius) { continue; }
                Vector2 size = dockingPort.Item.Rect.Size.ToVector2() * scale;

                if (dockingPort.IsHorizontal)
                {
                    size.X = 0.0f;
                }
                else
                {
                    size.Y = 0.0f;
                }
                GUI.DrawLine(spriteBatch, center + offset - size - Vector2.Normalize(size) * zoom, center + offset + size + Vector2.Normalize(size) * zoom, Color.Black * signalStrength * 0.5f, width: (int)(zoom * 5.0f));
                GUI.DrawLine(spriteBatch, center + offset - size, center + offset + size, positiveColor * signalStrength, width: (int)(zoom * 2.5f));
            }
        }

        private void DrawDockingIndicator(SpriteBatch spriteBatch, Steering steering, ref Vector2 transducerCenter)
        {
            float scale = displayScale * zoom;
            
            Vector2 worldFocusPos = (steering.ActiveDockingSource.Item.WorldPosition + steering.DockingTarget.Item.WorldPosition) / 2.0f;
            worldFocusPos.X = steering.DockingTarget.Item.WorldPosition.X;

            DisplayOffset = Vector2.Lerp(DisplayOffset, worldFocusPos - transducerCenter, 0.1f);
            transducerCenter += DisplayOffset;

            Vector2 sourcePortDiff = (steering.ActiveDockingSource.Item.WorldPosition - transducerCenter) * scale;
            Vector2 sourcePortPos = new Vector2(sourcePortDiff.X, -sourcePortDiff.Y);
            Vector2 targetPortDiff = (steering.DockingTarget.Item.WorldPosition - transducerCenter) * scale;
            Vector2 targetPortPos = new Vector2(targetPortDiff.X, -targetPortDiff.Y);

            System.Diagnostics.Debug.Assert(steering.ActiveDockingSource.IsHorizontal == steering.DockingTarget.IsHorizontal);
            Vector2 diff = steering.DockingTarget.Item.WorldPosition - steering.ActiveDockingSource.Item.WorldPosition;
            float dist = diff.Length();
            bool readyToDock = 
                Math.Abs(diff.X) < steering.DockingTarget.DistanceTolerance.X &&
                Math.Abs(diff.Y) < steering.DockingTarget.DistanceTolerance.Y;
                       
            Vector2 dockingDir = sourcePortPos - targetPortPos;
            Vector2 normalizedDockingDir = Vector2.Normalize(dockingDir);
            if (!dynamicDockingIndicator)
            {
                if (steering.ActiveDockingSource.IsHorizontal)
                {
                    normalizedDockingDir = new Vector2(Math.Sign(normalizedDockingDir.X), 0.0f);
                }
                else
                {
                    normalizedDockingDir = new Vector2(0.0f, Math.Sign(normalizedDockingDir.Y));
                }
            }

            Color staticLineColor = Color.White * 0.2f;

            float sector = MathHelper.ToRadians(MathHelper.Lerp(10.0f, 45.0f, MathHelper.Clamp(dist / steering.DockingAssistThreshold, 0.0f, 1.0f)));
            float sectorLength = DisplayRadius;
            //use law of cosines to calculate the length of the center line
            float midLength = (float)(Math.Cos(sector) * sectorLength);

            Vector2 midNormal = new Vector2(-normalizedDockingDir.Y, normalizedDockingDir.X);

            DrawLine(spriteBatch, targetPortPos, targetPortPos + normalizedDockingDir * midLength, readyToDock ? positiveColor : staticLineColor, width: 2);
            DrawLine(spriteBatch, targetPortPos,
                targetPortPos + MathUtils.RotatePoint(normalizedDockingDir, sector) * sectorLength, staticLineColor, width: 2);
            DrawLine(spriteBatch, targetPortPos,
                targetPortPos + MathUtils.RotatePoint(normalizedDockingDir, -sector) * sectorLength, staticLineColor, width: 2);

            for (float z = 0; z < 1.0f; z += 0.1f * zoom)
            {
                Vector2 linePos = targetPortPos + normalizedDockingDir * midLength * z;
                DrawLine(spriteBatch, linePos + midNormal * 3.0f, linePos - midNormal * 3.0f, staticLineColor, width: 3);
            }

            if (readyToDock)
            {
                Color indicatorColor = positiveColor * 0.8f;

                float indicatorSize = (float)Math.Sin((float)Timing.TotalTime * 5.0f) * DisplayRadius * 0.75f;
                Vector2 midPoint = (sourcePortPos + targetPortPos) / 2.0f;
                DrawLine(spriteBatch, 
                    midPoint + Vector2.UnitY * indicatorSize,
                    midPoint - Vector2.UnitY * indicatorSize, 
                    indicatorColor, width: 3);
                DrawLine(spriteBatch,
                    midPoint + Vector2.UnitX * indicatorSize,
                    midPoint - Vector2.UnitX * indicatorSize,
                    indicatorColor, width: 3);
            }
            else
            {
                float indicatorSector = sector * 0.75f;
                float indicatorSectorLength = (float)(midLength / Math.Cos(indicatorSector));

                    bool withinSector =
                    (Math.Abs(diff.X) < steering.ActiveDockingSource.DistanceTolerance.X && Math.Abs(diff.Y) < steering.ActiveDockingSource.DistanceTolerance.Y) ||
                    Vector2.Dot(normalizedDockingDir, MathUtils.RotatePoint(normalizedDockingDir, indicatorSector)) <
                    Vector2.Dot(normalizedDockingDir, Vector2.Normalize(dockingDir));

                Color indicatorColor = withinSector ? positiveColor : negativeColor;
                indicatorColor *= 0.8f;

                DrawLine(spriteBatch, targetPortPos,
                    targetPortPos + MathUtils.RotatePoint(normalizedDockingDir,indicatorSector) * indicatorSectorLength, indicatorColor, width: 3);
                DrawLine(spriteBatch, targetPortPos,
                    targetPortPos + MathUtils.RotatePoint(normalizedDockingDir, -indicatorSector) * indicatorSectorLength, indicatorColor, width: 3);
            }
            
        }

        private void UpdateDisruptions(Vector2 pingSource, float worldPingRadius)
        {
            float worldPingRadiusSqr = worldPingRadius * worldPingRadius;

            disruptedDirections.Clear();
            if (Level.Loaded == null) { return; }

            for (var pingIndex = 0; pingIndex < activePingsCount; ++pingIndex)
            {
                var activePing = activePings[pingIndex];
                foreach (LevelObject levelObject in Level.Loaded.LevelObjectManager.GetAllObjects(pingSource, range * activePing.State))
                {
                    if (levelObject.ActivePrefab?.SonarDisruption <= 0.0f) { continue; }

                    float disruptionStrength = levelObject.ActivePrefab.SonarDisruption;
                    Vector2 disruptionPos = new Vector2(levelObject.Position.X, levelObject.Position.Y);

                    float disruptionDist = Vector2.Distance(pingSource, disruptionPos);
                    disruptedDirections.Add(new Pair<Vector2, float>((disruptionPos - pingSource) / disruptionDist, disruptionStrength));

                    CreateBlipsForDisruption(disruptionPos, disruptionStrength);
                    
                }
                foreach (AITarget aiTarget in AITarget.List)
                {
                    float disruption = aiTarget.Entity is Character c ? c.Params.SonarDisruption : aiTarget.SonarDisruption;
                    if (disruption <= 0.0f || !aiTarget.Enabled) { continue; }
                    float distSqr = Vector2.DistanceSquared(aiTarget.WorldPosition, pingSource);
                    if (distSqr > worldPingRadiusSqr) { continue; }
                    float disruptionDist = (float)Math.Sqrt(distSqr);
                    disruptedDirections.Add(new Pair<Vector2, float>((aiTarget.WorldPosition - pingSource) / disruptionDist, aiTarget.SonarDisruption));
                    CreateBlipsForDisruption(aiTarget.WorldPosition, disruption);
                }
            }

            void CreateBlipsForDisruption(Vector2 disruptionPos, float disruptionStrength)
            {
                disruptionStrength = Math.Min(disruptionStrength, 10.0f);
                Vector2 dir = disruptionPos - pingSource;
                for (int i = 0; i < disruptionStrength * 10.0f; i++)
                {
                    Vector2 pos = disruptionPos + Rand.Vector(Rand.Range(0.0f, Level.GridCellSize * 4 * disruptionStrength));
                    if (Vector2.Dot(pos - pingSource, -dir) > 1.0f - disruptionStrength) { continue; }
                    var blip = new SonarBlip(
                        pos, 
                        MathHelper.Lerp(0.1f, 1.5f, Math.Min(disruptionStrength, 1.0f)), 
                        Rand.Range(0.2f, 1.0f + disruptionStrength),
                        BlipType.Disruption);
                    sonarBlips.Add(blip);
                }
            }
        }

        private void Ping(Vector2 pingSource, Vector2 transducerPos, float pingRadius, float prevPingRadius, float displayScale, float range, bool passive,
            float pingStrength = 1.0f)
        {
            float prevPingRadiusSqr = prevPingRadius * prevPingRadius;
            float pingRadiusSqr = pingRadius * pingRadius;
                        
            //inside a hull -> only show the edges of the hull
            if (item.CurrentHull != null && DetectSubmarineWalls)
            {
                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y), 
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y), 
                    pingSource, transducerPos,
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f, passive);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingSource, transducerPos,
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f, passive);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y),
                    new Vector2(item.CurrentHull.WorldRect.X, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingSource, transducerPos,
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f, passive);

                CreateBlipsForLine(
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y),
                    new Vector2(item.CurrentHull.WorldRect.Right, item.CurrentHull.WorldRect.Y - item.CurrentHull.Rect.Height),
                    pingSource, transducerPos,
                    pingRadius, prevPingRadius, 50.0f, 5.0f, range, 2.0f, passive);

                return;
            }

            foreach (Submarine submarine in Submarine.Loaded)
            {
                if (submarine.HullVertices == null) continue;
                if (!DetectSubmarineWalls)
                {
                    if (UseTransducers)
                    {
                        if (connectedTransducers.Any(t => submarine == t.Transducer.Item.Submarine || 
                            submarine.DockedTo.Contains(t.Transducer.Item.Submarine))) continue;
                    }
                    else
                    {
                        if (item.Submarine == submarine) continue;
                        if (item.Submarine != null && item.Submarine.DockedTo.Contains(submarine)) continue;
                    }
                }

                for (int i = 0; i < submarine.HullVertices.Count; i++)
                {
                    Vector2 start = ConvertUnits.ToDisplayUnits(submarine.HullVertices[i]);
                    Vector2 end = ConvertUnits.ToDisplayUnits(submarine.HullVertices[(i + 1) % submarine.HullVertices.Count]);

                    if (item.Submarine == submarine)
                    {
                        start += Rand.Vector(500.0f);
                        end += Rand.Vector(500.0f);
                    }

                    CreateBlipsForLine(
                        start + submarine.WorldPosition,
                        end + submarine.WorldPosition,
                        pingSource, transducerPos,
                        pingRadius, prevPingRadius,
                        200.0f, 2.0f, range, 1.0f, passive);
                }
            }

            if (Level.Loaded != null && (item.CurrentHull == null || !DetectSubmarineWalls))
            {
                if (Level.Loaded.Size.Y - pingSource.Y < range)
                {
                    CreateBlipsForLine(
                        new Vector2(pingSource.X - range, Level.Loaded.Size.Y),
                        new Vector2(pingSource.X + range, Level.Loaded.Size.Y),
                        pingSource, transducerPos,
                        pingRadius, prevPingRadius,
                        250.0f, 150.0f, range, pingStrength, passive);
                }

                List<Voronoi2.VoronoiCell> cells = Level.Loaded.GetCells(pingSource, 7);
                foreach (Voronoi2.VoronoiCell cell in cells)
                {
                    foreach (Voronoi2.GraphEdge edge in cell.Edges)
                    {
                        if (!edge.IsSolid) continue;
                        float cellDot = Vector2.Dot(cell.Center - pingSource, (edge.Center + cell.Translation) - cell.Center);
                        if (cellDot > 0) continue;

                        float facingDot = Vector2.Dot(
                            Vector2.Normalize(edge.Point1 - edge.Point2),
                            Vector2.Normalize(cell.Center - pingSource));

                        CreateBlipsForLine(
                            edge.Point1 + cell.Translation,
                            edge.Point2 + cell.Translation,
                            pingSource, transducerPos,
                            pingRadius, prevPingRadius,
                            350.0f, 3.0f * (Math.Abs(facingDot) + 1.0f), range, pingStrength, passive);
                    }
                }

                foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                {
                    if (!MathUtils.CircleIntersectsRectangle(pingSource, range, ruin.Area)) continue;

                    foreach (var ruinShape in ruin.RuinShapes)
                    {
                        foreach (RuinGeneration.Line wall in ruinShape.Walls)
                        {
                            float cellDot = Vector2.Dot(
                                Vector2.Normalize(ruinShape.Center - pingSource),
                                Vector2.Normalize((wall.A + wall.B) / 2.0f - ruinShape.Center));
                            if (cellDot > 0) continue;

                            CreateBlipsForLine(
                                wall.A, wall.B,
                                pingSource, transducerPos,
                                pingRadius, prevPingRadius,
                                100.0f, 1000.0f, range, pingStrength, passive);
                        }
                    }
                }
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull == null && item.Prefab.SonarSize > 0.0f)
                {
                    float pointDist = ((item.WorldPosition - pingSource) * displayScale).LengthSquared();

                    if (pointDist > prevPingRadiusSqr && pointDist < pingRadiusSqr)
                    {
                        var blip = new SonarBlip(
                            item.WorldPosition + Rand.Vector(item.Prefab.SonarSize),
                            MathHelper.Clamp(item.Prefab.SonarSize, 0.1f, pingStrength),
                            MathHelper.Clamp(item.Prefab.SonarSize * 0.1f, 0.1f, 10.0f));
                        if (!passive && !CheckBlipVisibility(blip, transducerPos)) continue;
                        sonarBlips.Add(blip);
                    }
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled) { continue; }
                if (c.Params.HideInSonar) { continue; }
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) { continue; }

                if (c.AnimController.SimplePhysicsEnabled)
                {
                    float pointDist = ((c.WorldPosition - pingSource) * displayScale).LengthSquared();
                    if (pointDist > DisplayRadius * DisplayRadius) { continue; }

                    if (pointDist > prevPingRadiusSqr && pointDist < pingRadiusSqr)
                    {
                        var blip = new SonarBlip(
                            c.WorldPosition,
                            MathHelper.Clamp(c.Mass, 0.1f, pingStrength),
                            MathHelper.Clamp(c.Mass * 0.03f, 0.1f, 2.0f));
                        if (!passive && !CheckBlipVisibility(blip, transducerPos)) { continue; }
                        sonarBlips.Add(blip);
                    }
                    continue;
                }

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (!limb.body.Enabled) { continue; }

                    float pointDist = ((limb.WorldPosition - pingSource) * displayScale).LengthSquared();
                    if (limb.SimPosition == Vector2.Zero || pointDist > DisplayRadius * DisplayRadius) { continue; }

                    if (pointDist > prevPingRadiusSqr && pointDist < pingRadiusSqr)
                    {
                        var blip = new SonarBlip(
                            limb.WorldPosition + Rand.Vector(limb.Mass / 10.0f), 
                            MathHelper.Clamp(limb.Mass, 0.1f, pingStrength), 
                            MathHelper.Clamp(limb.Mass * 0.1f, 0.1f, 2.0f));
                        if (!passive && !CheckBlipVisibility(blip, transducerPos)) { continue; }
                        sonarBlips.Add(blip);
                    }
                }
            }
        }
        
        private void CreateBlipsForLine(Vector2 point1, Vector2 point2, Vector2 pingSource, Vector2 transducerPos, float pingRadius, float prevPingRadius,
            float lineStep, float zStep, float range, float pingStrength, bool passive)
        {
            lineStep /= zoom;
            zStep /= zoom;
            range *= displayScale;
            float length = (point1 - point2).Length();
            Vector2 lineDir = (point2 - point1) / length;
            for (float x = 0; x < length; x += lineStep * Rand.Range(0.8f, 1.2f))
            {
                if (Rand.Int(sonarBlips.Count) > 500) { continue; }

                Vector2 point = point1 + lineDir * x;

                //ignore if outside the display
                Vector2 transducerDiff = point - transducerPos;
                Vector2 transducerDisplayDiff = transducerDiff * displayScale;
                if (transducerDisplayDiff.LengthSquared() > DisplayRadius * DisplayRadius) continue;

                //ignore if the point is not within the ping
                Vector2 pointDiff = point - pingSource;
                Vector2 displayPointDiff = pointDiff * displayScale;
                float displayPointDistSqr = displayPointDiff.LengthSquared();
                if (displayPointDistSqr < prevPingRadius * prevPingRadius || displayPointDistSqr > pingRadius * pingRadius) continue;

                //ignore if direction is disrupted
                float transducerDist = transducerDiff.Length();
                Vector2 pingDirection = transducerDiff / transducerDist;
                bool disrupted = false;
                foreach (Pair<Vector2, float> disruptDir in disruptedDirections)
                {
                    float dot = Vector2.Dot(pingDirection, disruptDir.First);
                    if (dot >  1.0f - disruptDir.Second)
                    {
                        disrupted = true;
                        break;
                    }
                }
                if (disrupted) continue;

                float displayPointDist = (float)Math.Sqrt(displayPointDistSqr);
                float alpha = pingStrength * Rand.Range(1.5f, 2.0f);
                for (float z = 0; z < DisplayRadius - transducerDist * displayScale; z += zStep)
                {
                    Vector2 pos = point + Rand.Vector(150.0f / zoom) + pingDirection * z / displayScale;
                    float fadeTimer = alpha * (1.0f - displayPointDist / range);

                    int minDist = (int)(200 / zoom);
                    sonarBlips.RemoveAll(b => b.FadeTimer < fadeTimer && Math.Abs(pos.X - b.Position.X) < minDist && Math.Abs(pos.Y - b.Position.Y) < minDist);

                    var blip = new SonarBlip(pos, fadeTimer, 1.0f + ((displayPointDist + z) / DisplayRadius));
                    if (!passive && !CheckBlipVisibility(blip, transducerPos)) continue;

                    sonarBlips.Add(blip);
                    zStep += 0.5f / zoom;

                    if (z == 0)
                    {
                        alpha = Math.Min(alpha - 0.5f, 1.5f);
                    }
                    else
                    {
                        alpha -= 0.1f;
                    }

                    if (alpha < 0) break;
                }
            }
        }

        private bool CheckBlipVisibility(SonarBlip blip, Vector2 transducerPos)
        {
            Vector2 pos = (blip.Position - transducerPos) * displayScale * zoom;
            pos.Y = -pos.Y;

            float posDistSqr = pos.LengthSquared();
            if (posDistSqr > DisplayRadius * DisplayRadius)
            {
                blip.FadeTimer = 0.0f;
                return false;
            }

            Vector2 dir = pos / (float)Math.Sqrt(posDistSqr);
            if (currentPingIndex != -1 && activePings[currentPingIndex].IsDirectional)
            {
                if (Vector2.Dot(activePings[currentPingIndex].Direction, dir) < DirectionalPingDotProduct)
                {
                    blip.FadeTimer = 0.0f;
                    return false;
                }
            }
            return true;
        }

        private void DrawBlip(SpriteBatch spriteBatch, SonarBlip blip, Vector2 transducerPos, Vector2 center, float strength)
        {
            strength = MathHelper.Clamp(strength, 0.0f, 1.0f);
            
            float distort = 1.0f - item.Condition / item.MaxCondition;
            
            Vector2 pos = (blip.Position - transducerPos) * displayScale * zoom;
            pos.Y = -pos.Y;

            if (Rand.Range(0.5f, 2.0f) < distort) pos.X = -pos.X;
            if (Rand.Range(0.5f, 2.0f) < distort) pos.Y = -pos.Y;

            float posDistSqr = pos.LengthSquared();
            if (posDistSqr > DisplayRadius * DisplayRadius)
            {
                blip.FadeTimer = 0.0f;
                return;
            }
            
            if (sonarBlip == null)
            {
                GUI.DrawRectangle(spriteBatch, center + pos, Vector2.One * 4, Color.Magenta, true);
                return;
            }

            Vector2 dir = pos / (float)Math.Sqrt(posDistSqr);
            Vector2 normal = new Vector2(dir.Y, -dir.X);
            float scale = (strength + 3.0f) * blip.Scale * zoomSqrt;
            Color color = ToolBox.GradientLerp(strength, blipColorGradient[blip.BlipType]);

            sonarBlip.Draw(spriteBatch, center + pos, color, sonarBlip.Origin, blip.Rotation ?? MathUtils.VectorToAngle(pos),
                blip.Size * scale * 0.04f, SpriteEffects.None, 0);

            pos += Rand.Range(0.0f, 1.0f) * dir + Rand.Range(-scale, scale) * normal;

            sonarBlip.Draw(spriteBatch, center + pos, color * 0.5f, sonarBlip.Origin, 0, scale * 0.08f, SpriteEffects.None, 0);
        }

        private void DrawMarker(SpriteBatch spriteBatch, string label, string iconIdentifier, object targetIdentifier, Vector2 worldPosition, Vector2 transducerPosition, float scale, Vector2 center, float radius)
        {
            float linearDist = Vector2.Distance(worldPosition, transducerPosition);
            float dist = linearDist;
            if (linearDist > Range)
            {
                if (markerDistances.TryGetValue(targetIdentifier, out CachedDistance cachedDistance))
                {
                    if (Timing.TotalTime > cachedDistance.RecalculationTime &&
                        (Vector2.DistanceSquared(cachedDistance.TransducerWorldPos, transducerPosition) > 500 * 500 ||
                        Vector2.DistanceSquared(cachedDistance.WorldPos, worldPosition) > 500 * 500))
                    {
                        markerDistances.Remove(targetIdentifier);
                        CalculateDistance();
                    }
                    else
                    {
                        dist = Math.Max(cachedDistance.Distance, linearDist);
                    }
                }
                else
                {
                    CalculateDistance();
                }
            }

            void CalculateDistance()
            {
                pathFinder ??= new PathFinder(WayPoint.WayPointList, indoorsSteering: false);
                var path = pathFinder.FindPath(ConvertUnits.ToSimUnits(transducerPosition), ConvertUnits.ToSimUnits(worldPosition));
                if (!path.Unreachable)
                {
                    var cachedDistance = new CachedDistance(transducerPosition, worldPosition, path.TotalLength)
                    {
                        RecalculationTime = Timing.TotalTime + Rand.Range(1.0f, 5.0f)
                    };
                    markerDistances.Add(targetIdentifier, cachedDistance);
                    dist = path.TotalLength;
                }
            }

            Vector2 position = worldPosition - transducerPosition;

            position *= zoom;
            position *= scale;
            position.Y = -position.Y;

            float textAlpha = MathHelper.Clamp(1.5f - dist / 50000.0f, 0.5f, 1.0f);

            Vector2 dir = Vector2.Normalize(position);
            Vector2 markerPos = (linearDist * zoom * scale > radius) ? dir * radius : position;
            markerPos += center;

            markerPos.X = (int)markerPos.X;
            markerPos.Y = (int)markerPos.Y;

            float alpha = 1.0f;
            if (linearDist * scale < radius)
            {
                float normalizedDist = linearDist * scale / radius;
                alpha = Math.Max(normalizedDist - 0.4f, 0.0f);

                float mouseDist = Vector2.Distance(PlayerInput.MousePosition, markerPos);
                float hoverThreshold = 150.0f;
                if (mouseDist < hoverThreshold)
                {
                    alpha += (hoverThreshold - mouseDist) / hoverThreshold;
                }
            }

            if (string.IsNullOrEmpty(iconIdentifier) || !targetIcons.ContainsKey(iconIdentifier))
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)markerPos.X - 3, (int)markerPos.Y - 3, 6, 6), markerColor, thickness: 2);
            }
            else
            {
                targetIcons[iconIdentifier].Draw(spriteBatch, markerPos);
            }

            if (alpha <= 0.0f) { return; }

            string wrappedLabel = ToolBox.WrapText(label, 150, GUI.SmallFont);
            wrappedLabel += "\n" + ((int)(dist * Physics.DisplayToRealWorldRatio) + " m");

            Vector2 labelPos = markerPos;
            Vector2 textSize = GUI.SmallFont.MeasureString(wrappedLabel);

            //flip the text to left side when the marker is on the left side or goes outside the right edge of the interface
            if ((dir.X < 0.0f || labelPos.X + textSize.X + 10 > GuiFrame.Rect.X) && labelPos.X - textSize.X > 0) labelPos.X -= textSize.X + 10;

            GUI.DrawString(spriteBatch,
                new Vector2(labelPos.X + 10, labelPos.Y),
                wrappedLabel,
                Color.LightBlue * textAlpha * alpha, Color.Black * textAlpha * 0.8f * alpha,
                2, GUI.SmallFont);
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            sonarBlip?.Remove();
            pingCircle?.Remove();
            directionalPingCircle?.Remove();
            screenOverlay?.Remove();
            screenBackground?.Remove();
            lineSprite?.Remove();

            foreach (Sprite sprite in targetIcons.Values)
            {
                sprite.Remove();
            }
            targetIcons.Clear();

            GameMain.Instance.OnResolutionChanged -= RecreateGUI;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write(currentMode == Mode.Active);
            if (currentMode == Mode.Active)
            {
                msg.WriteRangedSingle(zoom, MinZoom, MaxZoom, 8);
                msg.Write(useDirectionalPing);
                if (useDirectionalPing)
                {
                    float pingAngle = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(pingDirection));
                    msg.WriteRangedSingle(MathUtils.InverseLerp(0.0f, MathHelper.TwoPi, pingAngle), 0.0f, 1.0f, 8);
                }
            }
        }
        
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;

            bool isActive           = msg.ReadBoolean();
            float zoomT             = 1.0f;
            bool directionalPing    = useDirectionalPing;
            float directionT        = 0.0f;
            if (isActive)
            {
                zoomT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                directionalPing = msg.ReadBoolean();
                if (directionalPing)
                {
                    directionT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                }
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = msg.BitPosition - msgStartPos;
                msg.BitPosition = msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            CurrentMode = isActive ? Mode.Active : Mode.Passive;
            if (isActive)
            {
                zoomSlider.BarScroll = zoomT;
                zoom = MathHelper.Lerp(MinZoom, MaxZoom, zoomT);
                if (directionalPing)
                {
                    float pingAngle = MathHelper.Lerp(0.0f, MathHelper.TwoPi, directionT);
                    pingDirection = new Vector2((float)Math.Cos(pingAngle), (float)Math.Sin(pingAngle));
                }
                useDirectionalPing = directionalModeSwitch.Selected = directionalPing;
            }
        }

        private void UpdateGUIElements()
        {
            bool isActive = CurrentMode == Mode.Active;
            SonarModeSwitch.Selected = isActive;
            passiveTickBox.Selected = !isActive;
            activeTickBox.Selected = isActive;
            directionalModeSwitch.Selected = useDirectionalPing;
        }
    }

    class SonarBlip
    {
        public float FadeTimer;
        public Vector2 Position;
        public float Scale;
        public Vector2 Velocity;
        public float? Rotation;
        public Vector2 Size;
        public Sonar.BlipType BlipType;

        public SonarBlip(Vector2 pos, float fadeTimer, float scale, Sonar.BlipType blipType = Sonar.BlipType.Default)
        {
            Position = pos;
            FadeTimer = Math.Max(fadeTimer, 0.0f);
            Scale = scale;
            Size = new Vector2(0.5f, 1.0f);
            BlipType = blipType;
        }
    }
}
