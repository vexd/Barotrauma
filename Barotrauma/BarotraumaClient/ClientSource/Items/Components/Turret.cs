﻿using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite crosshairSprite, crosshairPointerSprite;

        private GUIProgressBar powerIndicator;

        private float recoilTimer;

        private float RetractionTime => Math.Max(Reload * RetractionDurationMultiplier, RecoilTime);

        private RoundSound startMoveSound, endMoveSound, moveSound;

        private SoundChannel moveSoundChannel;

        private Vector2 crosshairPos, crosshairPointerPos;

        private readonly Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();
        private float prevAngle;
        
        private bool flashLowPower;
        private bool flashNoAmmo;
        private float flashTimer;
        private float flashLength = 1;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();

        [Editable, Serialize("0,0,0,0", true, description: "Optional screen tint color when the item is being operated (R,G,B,A).")]
        public Color HudTint
        {
            get;
            private set;
        }

        [Serialize(false, false, description: "Should the charge of the connected batteries/supercapacitors be shown at the top of the screen when operating the item.")]
        public bool ShowChargeIndicator
        {
            get;
            private set;
        }

        [Serialize(false, false, description: "Should the available ammunition be shown at the top of the screen when operating the item.")]
        public bool ShowProjectileIndicator
        {
            get;
            private set;
        }

        [Serialize(0.0f, false, description: "How far the barrel \"recoils back\" when the turret is fired (in pixels).")]
        public float RecoilDistance
        {
            get;
            private set;
        }

        public Vector2 DrawSize
        {
            get
            {
                float size = Math.Max(transformedBarrelPos.X, transformedBarrelPos.Y);
                if (barrelSprite != null)
                {
                    if (railSprite != null)
                    {
                        size += Math.Max(Math.Max(barrelSprite.size.X, barrelSprite.size.Y), Math.Max(railSprite.size.X, railSprite.size.Y)) * item.Scale;
                    }
                    else
                    {
                        size += Math.Max(barrelSprite.size.X, barrelSprite.size.Y) * item.Scale;
                    }
                }
                return Vector2.One * size * 2;
            }
        }

        public Sprite BarrelSprite
        {
            get { return barrelSprite; }
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                string texturePath = subElement.GetAttributeString("texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crosshair":
                        crosshairSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.FilePath));
                        break;
                    case "crosshairpointer":
                        crosshairPointerSprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.FilePath));
                        break;
                    case "startmovesound":
                        startMoveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "endmovesound":
                        endMoveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "movesound":
                        moveSound = Submarine.LoadRoundSound(subElement, false);
                        break;
                    case "particleemitter":
                        particleEmitters.Add(new ParticleEmitter(subElement));
                        break;
                }
            }
            
            powerIndicator = new GUIProgressBar(new RectTransform(new Vector2(0.18f, 0.03f), GUI.Canvas, Anchor.TopCenter)
            {
                MinSize = new Point(100,20),
                RelativeOffset = new Vector2(0.0f, 0.01f)
            }, 
            barSize: 0.0f, style: "DeviceProgressBar");
        }

        public override void Move(Vector2 amount)
        {
            widgets.Clear();
        }

        partial void LaunchProjSpecific()
        {
            recoilTimer = RetractionTime;
            PlaySound(ActionType.OnUse);
            Vector2 particlePos = new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y);
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(1.0f, particlePos, hullGuess: null, angle: -rotation, particleRotation: rotation);
            }
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            recoilTimer -= deltaTime;
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            recoilTimer -= deltaTime;

            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y));
                Vector2 turretDir = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;

            if (Math.Abs(angularVelocity) > 0.1f)
            {
                if (moveSoundChannel == null && startMoveSound != null)
                {
                    moveSoundChannel = SoundPlayer.PlaySound(startMoveSound.Sound, item.WorldPosition, startMoveSound.Volume, startMoveSound.Range);
                }
                else if (moveSoundChannel == null || !moveSoundChannel.IsPlaying)
                {
                    if (moveSound != null)
                    {
                        moveSoundChannel.FadeOutAndDispose();
                        moveSoundChannel = SoundPlayer.PlaySound(moveSound.Sound, item.WorldPosition, moveSound.Volume, moveSound.Range);
                        if (moveSoundChannel != null) moveSoundChannel.Looping = true;
                    }
                }
            }
            else if (Math.Abs(angularVelocity) < 0.05f)
            {
                if (moveSoundChannel != null)
                {
                    if (endMoveSound != null && moveSoundChannel.Sound != endMoveSound.Sound)
                    {
                        moveSoundChannel.FadeOutAndDispose();
                        moveSoundChannel = SoundPlayer.PlaySound(endMoveSound.Sound, item.WorldPosition, endMoveSound.Volume, endMoveSound.Range);
                        if (moveSoundChannel != null) moveSoundChannel.Looping = false;
                    }
                    else if (!moveSoundChannel.IsPlaying)
                    {
                        moveSoundChannel.FadeOutAndDispose();
                        moveSoundChannel = null;

                    }
                }
            }

            if (moveSoundChannel != null && moveSoundChannel.IsPlaying)
            {
                moveSoundChannel.Gain = MathHelper.Clamp(Math.Abs(angularVelocity), 0.5f, 1.0f);
            }

            if (flashLowPower || flashNoAmmo)
            {
                flashTimer += deltaTime;
                if (flashTimer >= flashLength)
                {
                    flashTimer = 0;
                    flashLowPower = false;
                    flashNoAmmo = false;
                }
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(item.WorldPosition);
                Vector2 turretDir = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;
        }
        
        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            Vector2 drawPos = new Vector2(item.Rect.X + transformedBarrelPos.X, item.Rect.Y - transformedBarrelPos.Y);
            if (item.Submarine != null)
            {
                drawPos += item.Submarine.DrawPosition;
            }
            drawPos.Y = -drawPos.Y;

            float recoilOffset = 0.0f;
            if (Math.Abs(RecoilDistance) > 0.0f && recoilTimer > 0.0f)
            {
                float diff = RetractionTime - RecoilTime;
                if (recoilTimer >= diff)
                {
                    //move the barrel backwards 0.1 seconds (defined by RecoilTime) after launching
                    recoilOffset = RecoilDistance * (1.0f - (recoilTimer - diff) / RecoilTime);
                }
                else if (recoilTimer <= diff - RetractionDelay)
                {
                    //move back to normal position while reloading
                    float t = diff - RetractionDelay;
                    recoilOffset = RecoilDistance * recoilTimer / t;
                }
                else
                {
                    recoilOffset = RecoilDistance;
                }
            }

            railSprite?.Draw(spriteBatch,
                drawPos,
                item.SpriteColor,
                rotation + MathHelper.PiOver2, item.Scale,
                SpriteEffects.None, item.SpriteDepth + (railSprite.Depth - item.Sprite.Depth));

            barrelSprite?.Draw(spriteBatch,
                drawPos - new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation)) * recoilOffset * item.Scale,
                item.SpriteColor,
                rotation + MathHelper.PiOver2, item.Scale,
                SpriteEffects.None, item.SpriteDepth + (barrelSprite.Depth - item.Sprite.Depth));

            if (!editing) { return; }

            float widgetRadius = 60.0f;

            GUI.DrawLine(spriteBatch,
                drawPos,
                drawPos + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation)) * widgetRadius,
                GUI.Style.Green);

            GUI.DrawLine(spriteBatch,
                drawPos,
                drawPos + new Vector2((float)Math.Cos(maxRotation), (float)Math.Sin(maxRotation)) * widgetRadius,
                GUI.Style.Green);

            GUI.DrawLine(spriteBatch,
                drawPos,
                drawPos + new Vector2((float)Math.Cos((maxRotation + minRotation) / 2), (float)Math.Sin((maxRotation + minRotation) / 2)) * widgetRadius,
                Color.LightGreen);

            if (!item.IsSelected) { return; }

            Widget minRotationWidget = GetWidget("minrotation", spriteBatch, size: 10, initMethod: (widget) =>
             {
                 widget.MouseDown += () =>
                 {
                     widget.color = GUI.Style.Green;
                     prevAngle = minRotation;
                 };
                 widget.Deselected += () =>
                 {
                     widget.color = Color.Yellow;
                     item.CreateEditingHUD();
                 };
                 widget.MouseHeld += (deltaTime) =>
                 {
                     minRotation = GetRotationAngle(drawPos);
                     if (minRotation > maxRotation)
                     {
                         float temp = minRotation;
                         minRotation = maxRotation;
                         maxRotation = temp;
                     }
                     MapEntity.DisableSelect = true;
                 };
                 widget.PreUpdate += (deltaTime) =>
                 {
                     widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                     widget.DrawPos = Screen.Selected.Cam.WorldToScreen(widget.DrawPos);
                 };
                 widget.PostUpdate += (deltaTime) =>
                 {
                     widget.DrawPos = Screen.Selected.Cam.ScreenToWorld(widget.DrawPos);
                     widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                 };
                 widget.PreDraw += (sprtBtch, deltaTime) =>
                 {
                     widget.tooltip = "Min: " + (int)MathHelper.ToDegrees(minRotation);
                     widget.DrawPos = drawPos + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation)) * widgetRadius;
                     widget.Update(deltaTime);
                 };
             });
            
            Widget maxRotationWidget = GetWidget("maxrotation", spriteBatch, size: 10, initMethod: (widget) =>
            {
                widget.MouseDown += () =>
                {
                    widget.color = GUI.Style.Green;
                    prevAngle = minRotation;
                };
                widget.Deselected += () =>
                {
                    widget.color = Color.Yellow;
                    item.CreateEditingHUD();
                };
                widget.MouseHeld += (deltaTime) =>
                {
                    maxRotation = GetRotationAngle(drawPos);
                    if (minRotation > maxRotation)
                    {
                        float temp = minRotation;
                        minRotation = maxRotation;
                        maxRotation = temp;
                    }
                    MapEntity.DisableSelect = true;
                };
                widget.PreUpdate += (deltaTime) =>
                {
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                    widget.DrawPos = Screen.Selected.Cam.WorldToScreen(widget.DrawPos);
                };
                widget.PostUpdate += (deltaTime) =>
                {
                    widget.DrawPos = Screen.Selected.Cam.ScreenToWorld(widget.DrawPos);
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                };
                widget.PreDraw += (sprtBtch, deltaTime) =>
                {
                    widget.tooltip = "Max: " + (int)MathHelper.ToDegrees(maxRotation);
                    widget.DrawPos = drawPos + new Vector2((float)Math.Cos(maxRotation), (float)Math.Sin(maxRotation)) * widgetRadius;
                    widget.Update(deltaTime);
                };
            });
            minRotationWidget.Draw(spriteBatch, (float)Timing.Step);
            maxRotationWidget.Draw(spriteBatch, (float)Timing.Step);
        }

        private Widget GetWidget(string id, SpriteBatch spriteBatch, int size = 5, Action<Widget> initMethod = null)
        {
            if (!widgets.TryGetValue(id, out Widget widget))
            {
                widget = new Widget(id, size, Widget.Shape.Rectangle)
                {
                    color = Color.Yellow,
                    tooltipOffset = new Vector2(size / 2 + 5, -10),
                    inputAreaMargin = 20,
                    RequireMouseOn = false
                };
                widgets.Add(id, widget);
                initMethod?.Invoke(widget);
            }
            return widget;
        }

        private void GetAvailablePower(out float availableCharge, out float availableCapacity)
        {
            var batteries = item.GetConnectedComponents<PowerContainer>();

            availableCharge = 0.0f;
            availableCapacity = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                availableCharge += battery.Charge;
                availableCapacity += battery.Capacity;
            }
        }

        /// <summary>
        /// Returns correct angle between -2PI and +2PI
        /// </summary>
        /// <param name="drawPosition"></param>
        /// <returns></returns>
        private float GetRotationAngle(Vector2 drawPosition)
        {
            Vector2 mouseVector = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
            mouseVector.Y = -mouseVector.Y;
            Vector2 rotationVector = mouseVector - drawPosition;
            rotationVector.Normalize();
            double angle = Math.Atan2(MathHelper.ToRadians(rotationVector.Y), MathHelper.ToRadians(rotationVector.X));
            if (angle < 0)
            {// calculates which coterminal angle is closer to previous angle
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle + Math.PI * 2) - prevAngle) ? angle : angle + Math.PI * 2;
            }
            else if (angle > 0)
            {
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle - Math.PI * 2) - prevAngle) ? angle : angle - Math.PI * 2;
            }
            angle = MathHelper.Clamp((float)angle, -((float)Math.PI * 2), (float)Math.PI * 2);
            prevAngle = (float)angle;
            return (float)angle;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (HudTint.A > 0)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    new Color(HudTint.R, HudTint.G, HudTint.B) * (HudTint.A / 255.0f), true);
            }
            
            GetAvailablePower(out float batteryCharge, out float batteryCapacity);

            List<Item> availableAmmo = new List<Item>();
            foreach (MapEntity e in item.linkedTo)
            {
                var linkedItem = e as Item;
                if (linkedItem == null) continue;

                var itemContainer = linkedItem.GetComponent<ItemContainer>();
                if (itemContainer?.Inventory?.Items == null) continue;   
                
                availableAmmo.AddRange(itemContainer.Inventory.Items);                
            }            
                        
            float chargeRate = 
                powerConsumption <= 0.0f ? 
                1.0f : 
                batteryCapacity > 0.0f ? batteryCharge / batteryCapacity : 0.0f;
            bool charged = batteryCharge * 3600.0f > powerConsumption;
            bool readyToFire = reload <= 0.0f && charged && availableAmmo.Any(p => p != null);
            if (ShowChargeIndicator && PowerConsumption > 0.0f)
            {
                powerIndicator.Color = charged ? GUI.Style.Green : GUI.Style.Red;
                if (flashLowPower)
                {
                    powerIndicator.BarSize = 1;
                    powerIndicator.Color *= (float)Math.Sin(flashTimer * 12);
                    powerIndicator.RectTransform.ChangeScale(Vector2.Lerp(Vector2.One, Vector2.One * 1.01f, 2 * (float)Math.Sin(flashTimer * 15)));
                }
                else
                {
                    powerIndicator.BarSize = chargeRate;
                }
                powerIndicator.DrawManually(spriteBatch, true);

                Rectangle sliderRect = powerIndicator.GetSliderRect(1.0f);
                int requiredChargeIndicatorPos = (int)(powerConsumption / (batteryCapacity * 3600.0f) * sliderRect.Width);
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(sliderRect.X + requiredChargeIndicatorPos, sliderRect.Y, 2, sliderRect.Height),
                    Color.White * 0.5f, true);
            }

            if (ShowProjectileIndicator)
            {
                Point slotSize = (Inventory.SlotSpriteSmall.size * Inventory.UIScale).ToPoint();
                int spacing = 5;
                int slotsPerRow = Math.Min(availableAmmo.Count, 6);
                int totalWidth = slotSize.X * slotsPerRow + spacing * (slotsPerRow - 1);
                Point invSlotPos = new Point(GameMain.GraphicsWidth / 2 - totalWidth / 2, (int)(60 * GUI.Scale));
                for (int i = 0; i < availableAmmo.Count; i++)
                {
                    // TODO: Optimize? Creates multiple new objects per frame?
                    Inventory.DrawSlot(spriteBatch, null,
                        new InventorySlot(new Rectangle(invSlotPos + new Point((i % slotsPerRow) * (slotSize.X + spacing), (int)Math.Floor(i / (float)slotsPerRow) * (slotSize.Y + spacing)), slotSize)),
                        availableAmmo[i], -1, true);
                }
                if (flashNoAmmo)
                {
                    Rectangle rect = new Rectangle(invSlotPos.X, invSlotPos.Y, totalWidth, slotSize.Y);
                    float inflate = MathHelper.Lerp(3, 8, (float)Math.Abs(1 * Math.Sin(flashTimer * 5)));
                    rect.Inflate(inflate, inflate);
                    Color color = GUI.Style.Red * MathHelper.Max(0.5f, (float)Math.Sin(flashTimer * 12));
                    GUI.DrawRectangle(spriteBatch, rect, color, thickness: 3);
                }
            }

            float zoom = cam == null ? 1.0f : (float)Math.Sqrt(cam.Zoom);

            GUI.HideCursor = (crosshairSprite != null || crosshairPointerSprite != null) && GUI.MouseOn == null && !GameMain.Instance.Paused;
            if (GUI.HideCursor)
            {
                crosshairSprite?.Draw(spriteBatch, crosshairPos, readyToFire ? Color.White : Color.White * 0.2f, 0, zoom);
                crosshairPointerSprite?.Draw(spriteBatch, crosshairPointerPos, 0, zoom);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            UInt16 projectileID = msg.ReadUInt16();
            float newTargetRotation = msg.ReadRangedSingle(minRotation, maxRotation, 16);

            if (Character.Controlled == null || user != Character.Controlled)
            {
                targetRotation = newTargetRotation;
            }

            //projectile removed, do nothing
            if (projectileID == 0) { return; }

            //ID ushort.MaxValue = launched without a projectile
            if (projectileID == ushort.MaxValue)
            {
                Launch(null);
            }
            else
            {
                if (!(Entity.FindEntityByID(projectileID) is Item projectile))
                {
                    DebugConsole.ThrowError("Failed to launch a projectile - item with the ID \"" + projectileID + " not found");
                    return;
                }
                Launch(projectile, launchRotation: newTargetRotation);
            }

        }
    }
}
