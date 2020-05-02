﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public static bool ShowItems = true;
        
        private readonly List<PosInfo> positionBuffer = new List<PosInfo>();

        private readonly List<ItemComponent> activeHUDs = new List<ItemComponent>();

        public IEnumerable<ItemComponent> ActiveHUDs => activeHUDs;

        public float LastImpactSoundTime;
        public const float ImpactSoundInterval = 0.2f;

        private bool editingHUDRefreshPending;
        private float editingHUDRefreshTimer;

        public void Configure()
        {
            foreach(var component in components)
            {
                if (component.CanBeConfigured)
                    component.ShowConfigurationUI();
            }
        }

        private readonly Dictionary<DecorativeSprite, DecorativeSprite.State> spriteAnimState = new Dictionary<DecorativeSprite, DecorativeSprite.State>();

        private Sprite activeSprite;
        public override Sprite Sprite
        {
            get { return activeSprite; }
        }

        public override Rectangle Rect
        {
            get { return base.Rect; }
            set
            {
                cachedVisibleSize = null;
                base.Rect = value;
            }
        }

        public override bool DrawBelowWater => (!(Screen.Selected is SubEditorScreen editor) || !editor.WiringMode || !isWire) && base.DrawBelowWater;

        public override bool DrawOverWater => base.DrawOverWater || (IsSelected || Screen.Selected is SubEditorScreen editor && editor.WiringMode) && isWire;

        private GUITextBlock itemInUseWarning;
        private GUITextBlock ItemInUseWarning
        {
            get
            {
                if (itemInUseWarning == null)
                {
                    itemInUseWarning = new GUITextBlock(new RectTransform(new Point(10), GUI.Canvas), "", 
                        textColor: GUI.Style.Orange, color: Color.Black, 
                        textAlignment: Alignment.Center, style: "OuterGlow");
                }
                return itemInUseWarning;
            }
        }

        public override bool SelectableInEditor
        {
            get
            {
                return parentInventory == null && (body == null || body.Enabled) && ShowItems;
            }
        }
              
        public float SpriteRotation;

        public Color GetSpriteColor()
        {
            Color color = spriteColor;
            if (Prefab.UseContainedSpriteColor && ownInventory != null)
            {
                for (int i = 0; i < ownInventory.Items.Length; i++)
                {
                    if (ownInventory.Items[i] != null)
                    {
                        color = ownInventory.Items[i].ContainerColor;
                        break;
                    }
                }
            }
            return color;
        }

        public Color GetInventoryIconColor()
        {
            Color color = InventoryIconColor;
            if (Prefab.UseContainedInventoryIconColor && ownInventory != null)
            {
                for (int i = 0; i < ownInventory.Items.Length; i++)
                {
                    if (ownInventory.Items[i] != null)
                    {
                        color = ownInventory.Items[i].ContainerColor;
                        break;
                    }
                }
            }
            return color;
        }

        partial void SetActiveSpriteProjSpecific()
        {
            activeSprite = prefab.sprite;
            Holdable holdable = GetComponent<Holdable>();
            if (holdable != null && holdable.Attached)
            {
                foreach (ContainedItemSprite containedSprite in Prefab.ContainedSprites)
                {
                    if (containedSprite.UseWhenAttached)
                    {
                        activeSprite = containedSprite.Sprite;
                        return;
                    }
                }
            }

            if (Container != null)
            {
                foreach (ContainedItemSprite containedSprite in Prefab.ContainedSprites)
                {
                    if (containedSprite.MatchesContainer(Container))
                    {
                        activeSprite = containedSprite.Sprite;
                        return;
                    }
                }
            }

            for (int i = 0; i < Prefab.BrokenSprites.Count;i++)
            {
                float minCondition = i > 0 ? Prefab.BrokenSprites[i - i].MaxCondition : 0.0f;
                if (condition <= minCondition ||
                    condition <= Prefab.BrokenSprites[i].MaxCondition && !Prefab.BrokenSprites[i].FadeIn)
                {
                    activeSprite = Prefab.BrokenSprites[i].Sprite;
                    break;
                }
            }
        }

        partial void InitProjSpecific()
        {
            Prefab.sprite?.EnsureLazyLoaded();
            Prefab.InventoryIcon?.EnsureLazyLoaded();
            foreach (BrokenItemSprite brokenSprite in Prefab.BrokenSprites)
            {
                brokenSprite.Sprite.EnsureLazyLoaded();
            }
            
            foreach (var decorativeSprite in ((ItemPrefab)prefab).DecorativeSprites)
            {
                decorativeSprite.Sprite.EnsureLazyLoaded();
                spriteAnimState.Add(decorativeSprite, new DecorativeSprite.State());
            }
            UpdateSpriteStates(0.0f);
        }

        private Vector2? cachedVisibleSize;

        public void ResetCachedVisibleSize()
        {
            cachedVisibleSize = null;
        }

        public override bool IsVisible(Rectangle worldView)
        {
            // Inside of a container
            if (container != null)
            {
                return false;
            }

            //no drawable components and the body has been disabled = nothing to draw
            if (!hasComponentsToDraw && body != null && !body.Enabled)
            {
                return false;
            }

            Vector2 size;
            if (cachedVisibleSize.HasValue)
            {
                size = cachedVisibleSize.Value;
            }
            else
            {
                float padding = 100.0f;
                size = new Vector2(rect.Width + padding, rect.Height + padding);
                foreach (IDrawableComponent drawable in drawableComponents)
                {
                    size.X = Math.Max(drawable.DrawSize.X, size.X);
                    size.Y = Math.Max(drawable.DrawSize.Y, size.Y);
                }
                size *= 0.5f;
                cachedVisibleSize = size;
            }

            //cache world position so we don't need to calculate it 4 times
            Vector2 worldPosition = WorldPosition;
            if (worldPosition.X - size.X > worldView.Right || worldPosition.X + size.X < worldView.X) return false;
            if (worldPosition.Y + size.Y < worldView.Y - worldView.Height || worldPosition.Y - size.Y > worldView.Y) return false;

            return true;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!Visible || (!editing && HiddenInGame)) { return; }
            if (editing && !ShowItems) { return; }
            
            Color color = IsHighlighted && !GUI.DisableItemHighlights && Screen.Selected != GameMain.GameScreen ? GUI.Style.Orange : GetSpriteColor();
            //if (IsSelected && editing) color = Color.Lerp(color, Color.Gold, 0.5f);
            
            BrokenItemSprite fadeInBrokenSprite = null;
            float fadeInBrokenSpriteAlpha = 0.0f;
            if (condition < Prefab.Health)
            {
                for (int i = 0; i < Prefab.BrokenSprites.Count; i++)
                {
                    if (Prefab.BrokenSprites[i].FadeIn)
                    {
                        float min = i > 0 ? Prefab.BrokenSprites[i - i].MaxCondition : 0.0f;
                        float max = Prefab.BrokenSprites[i].MaxCondition;
                        fadeInBrokenSpriteAlpha = 1.0f - ((condition - min) / (max - min));
                        if (fadeInBrokenSpriteAlpha > 0.0f && fadeInBrokenSpriteAlpha < 1.0f)
                        {
                            fadeInBrokenSprite = Prefab.BrokenSprites[i];
                        }
                        continue;
                    }
                    if (condition <= Prefab.BrokenSprites[i].MaxCondition)
                    {
                        activeSprite = Prefab.BrokenSprites[i].Sprite;
                        break;
                    }
                }
            }

            float depth = GetDrawDepth();
            if (activeSprite != null)
            {
                SpriteEffects oldEffects = activeSprite.effects;
                activeSprite.effects ^= SpriteEffects;
                SpriteEffects oldBrokenSpriteEffects = SpriteEffects.None;
                if (fadeInBrokenSprite != null && fadeInBrokenSprite.Sprite != activeSprite)
                {
                    oldBrokenSpriteEffects = fadeInBrokenSprite.Sprite.effects;
                    fadeInBrokenSprite.Sprite.effects ^= SpriteEffects;
                }

                if (body == null)
                {
                    if (prefab.ResizeHorizontal || prefab.ResizeVertical)
                    {
                        activeSprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)), new Vector2(rect.Width, rect.Height), color: color,
                            depth: depth);
                        fadeInBrokenSprite?.Sprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)), new Vector2(rect.Width, rect.Height), color: color * fadeInBrokenSpriteAlpha,
                            depth: depth - 0.000001f);
                        foreach (var decorativeSprite in Prefab.DecorativeSprites)
                        {
                            if (!spriteAnimState[decorativeSprite].IsActive) { continue; }                            
                            Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState) * Scale;
                            decorativeSprite.Sprite.DrawTiled(spriteBatch, 
                                new Vector2(DrawPosition.X + offset.X - rect.Width / 2, -(DrawPosition.Y + offset.Y + rect.Height / 2)), 
                                new Vector2(rect.Width, rect.Height), color: color,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth), 0.999f));
                        }
                    }
                    else
                    {
                        activeSprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, SpriteRotation, Scale, activeSprite.effects, depth);
                        fadeInBrokenSprite?.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color * fadeInBrokenSpriteAlpha, SpriteRotation, Scale, activeSprite.effects, depth - 0.000001f);
                        foreach (var decorativeSprite in Prefab.DecorativeSprites)
                        {
                            if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                            float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState);
                            Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState) * Scale;
                            decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X + offset.X, -(DrawPosition.Y + offset.Y)), color, 
                                SpriteRotation + rotation, decorativeSprite.Scale * Scale, activeSprite.effects,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth), 0.999f));
                        }
                    }
                }
                else if (body.Enabled)
                {
                    var holdable = GetComponent<Holdable>();
                    if (holdable != null && holdable.Picker?.AnimController != null)
                    {
                        float depthStep = 0.000001f;
                        if (holdable.Picker.SelectedItems[0] == this)
                        {
                            Limb holdLimb = holdable.Picker.AnimController.GetLimb(LimbType.RightHand);
                            depth = holdLimb.ActiveSprite.Depth + holdable.Picker.AnimController.GetDepthOffset() + depthStep * 2;
                            foreach (WearableSprite wearableSprite in holdLimb.WearingItems)
                            {
                                if (!wearableSprite.InheritLimbDepth && wearableSprite.Sprite != null) { depth = Math.Max(wearableSprite.Sprite.Depth + depthStep, depth); }
                            }
                        }
                        else if (holdable.Picker.SelectedItems[1] == this)
                        {
                            Limb holdLimb = holdable.Picker.AnimController.GetLimb(LimbType.LeftHand);
                            depth = holdLimb.ActiveSprite.Depth + holdable.Picker.AnimController.GetDepthOffset() - depthStep * 2;
                            foreach (WearableSprite wearableSprite in holdLimb.WearingItems)
                            {
                                if (!wearableSprite.InheritLimbDepth && wearableSprite.Sprite != null) { depth = Math.Min(wearableSprite.Sprite.Depth - depthStep, depth); }
                            }
                        }
                    }
                    body.Draw(spriteBatch, activeSprite, color, depth, Scale);
                    if (fadeInBrokenSprite != null) { body.Draw(spriteBatch, fadeInBrokenSprite.Sprite, color * fadeInBrokenSpriteAlpha, depth - 0.000001f, Scale); }

                    foreach (var decorativeSprite in Prefab.DecorativeSprites)
                    {
                        if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                        float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState);
                        Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState) * Scale;

                        var ca = (float)Math.Cos(-body.Rotation);
                        var sa = (float)Math.Sin(-body.Rotation);
                        Vector2 transformedOffset = new Vector2(ca * offset.X + sa * offset.Y, -sa * offset.X + ca * offset.Y);

                        decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(DrawPosition.X + transformedOffset.X, -(DrawPosition.Y + transformedOffset.Y)), color,
                            -body.Rotation + rotation, decorativeSprite.Scale * Scale, activeSprite.effects,
                            depth: depth + (decorativeSprite.Sprite.Depth - activeSprite.Depth));
                    }
                }

                activeSprite.effects = oldEffects;
                if (fadeInBrokenSprite != null && fadeInBrokenSprite.Sprite != activeSprite)
                {
                    fadeInBrokenSprite.Sprite.effects = oldBrokenSpriteEffects;
                }
            }

            //use a backwards for loop because the drawable components may disable drawing, 
            //causing them to be removed from the list
            for (int i = drawableComponents.Count - 1; i >= 0; i--)
            {
                drawableComponents[i].Draw(spriteBatch, editing, depth);
            }

            if (GameMain.DebugDraw)
            {
                if (body != null)
                {
                    body.DebugDraw(spriteBatch, Color.White);
                }
            }

            if (!editing || (body != null && !body.Enabled))
            {
                return;
            }

            if (IsSelected || IsHighlighted)
            {
                GUI.DrawRectangle(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y + rect.Height / 2)), new Vector2(rect.Width, rect.Height), 
                    Color.White, false, 0, thickness: Math.Max(1, (int)(2 / Screen.Selected.Cam.Zoom)));

                foreach (Rectangle t in Prefab.Triggers)
                {
                    Rectangle transformedTrigger = TransformTrigger(t);

                    Vector2 rectWorldPos = new Vector2(transformedTrigger.X, transformedTrigger.Y);
                    if (Submarine != null) rectWorldPos += Submarine.Position;
                    rectWorldPos.Y = -rectWorldPos.Y;

                    GUI.DrawRectangle(spriteBatch,
                        rectWorldPos,
                        new Vector2(transformedTrigger.Width, transformedTrigger.Height),
                        GUI.Style.Green,
                        false,
                        0,
                        (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
                }
            }

            if (!ShowLinks) return;

            foreach (MapEntity e in linkedTo)
            {
                bool isLinkAllowed = prefab.IsLinkAllowed(e.prefab);
                Color lineColor = GUI.Style.Red * 0.5f;
                if (isLinkAllowed)
                {
                    lineColor = e is Item i && (DisplaySideBySideWhenLinked || i.DisplaySideBySideWhenLinked) ? Color.Purple * 0.5f : Color.LightGreen * 0.5f;
                }
                Vector2 from = new Vector2(WorldPosition.X, -WorldPosition.Y);
                Vector2 to = new Vector2(e.WorldPosition.X, -e.WorldPosition.Y);
                GUI.DrawLine(spriteBatch, from, to, lineColor * 0.25f, width: 3);
                GUI.DrawLine(spriteBatch, from, to, lineColor, width: 1);
                //GUI.DrawString(spriteBatch, from, $"Linked to {e.Name}", lineColor, Color.Black * 0.5f);
            }
        }

        partial void OnCollisionProjSpecific(float impact)
        {
            if (impact > 1.0f &&
                !string.IsNullOrEmpty(Prefab.ImpactSoundTag) &&
                Timing.TotalTime > LastImpactSoundTime + ImpactSoundInterval)
            {
                LastImpactSoundTime = (float)Timing.TotalTime;
                SoundPlayer.PlaySound(Prefab.ImpactSoundTag, WorldPosition, hullGuess: CurrentHull);
            }
        }

        public void UpdateSpriteStates(float deltaTime)
        {
            DecorativeSprite.UpdateSpriteStates(Prefab.DecorativeSpriteGroups, spriteAnimState, ID, deltaTime, ConditionalMatches);
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData as Item != this || 
                (editingHUDRefreshPending && editingHUDRefreshTimer <= 0.0f))
            {
                editingHUD = CreateEditingHUD(Screen.Selected != GameMain.SubEditorScreen);
                editingHUDRefreshTimer = 1.0f;
            }

            if (Screen.Selected != GameMain.SubEditorScreen) { return; }

            if (Character.Controlled == null) { activeHUDs.Clear(); }

            if (!Linkable) { return; }

            if (!PlayerInput.KeyDown(Keys.Space)) { return; }
            bool lClick = PlayerInput.PrimaryMouseButtonClicked();
            bool rClick = PlayerInput.SecondaryMouseButtonClicked();
            if (!lClick && !rClick) { return; }

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
            var otherEntity = mapEntityList.FirstOrDefault(e => e != this && e.IsHighlighted && e.IsMouseOn(position));
            if (otherEntity != null)
            {
                if (linkedTo.Contains(otherEntity))
                {
                    linkedTo.Remove(otherEntity);
                    if (otherEntity.linkedTo != null && otherEntity.linkedTo.Contains(this)) otherEntity.linkedTo.Remove(this);
                }
                else
                {
                    linkedTo.Add(otherEntity);
                    if (otherEntity.Linkable && otherEntity.linkedTo != null) otherEntity.linkedTo.Add(this);
                }
            }
        }

        public GUIComponent CreateEditingHUD(bool inGame = false)
        {
            editingHUDRefreshPending = false;

            int heightScaled = (int)(20 * GUI.Scale);
            editingHUD = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GUI.Canvas, Anchor.CenterRight) { MinSize = new Point(400, 0) }) { UserData = this };
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.8f), editingHUD.RectTransform, Anchor.Center), style: null)
            {
                Spacing = (int)(25 * GUI.Scale)
            };

            var itemEditor = new SerializableEntityEditor(listBox.Content.RectTransform, this, inGame, showName: true, titleFont: GUI.LargeFont);
            itemEditor.Children.First().Color = Color.Black * 0.7f;
            if (!inGame)
            {
                //create a tag picker for item containers to make it easier to pick relevant tags for PreferredContainers
                var itemContainer = GetComponent<ItemContainer>();
                if (itemContainer != null)
                {
                    var tagsField = itemEditor.Fields["Tags"].First().Parent;

                    //find all the items that can be put inside the container and add their PreferredContainer identifiers/tags to the available tags
                    HashSet<string> availableTags = new HashSet<string>();
                    foreach (MapEntityPrefab me in MapEntityPrefab.List)
                    {
                        if (!(me is ItemPrefab ip)) { continue; }
                        if (!itemContainer.CanBeContained(ip)) { continue; }
                        foreach (string tag in ip.PreferredContainers.SelectMany(pc => pc.Primary)) { availableTags.Add(tag); }
                        foreach (string tag in ip.PreferredContainers.SelectMany(pc => pc.Secondary)) { availableTags.Add(tag); }
                    }
                    //remove identifiers from the available container tags 
                    //(otherwise the list will include many irrelevant options, 
                    //e.g. "weldingtool" because a welding fuel tank can be placed inside the container, etc)
                    availableTags.RemoveWhere(t => MapEntityPrefab.List.Any(me => me.Identifier == t));
                    new GUIButton(new RectTransform(new Vector2(0.1f, 1), tagsField.RectTransform, Anchor.TopRight), "...")
                    {
                        OnClicked = (bt, userData) => { CreateTagPicker(tagsField.GetChild<GUITextBox>(), availableTags); return true; }
                    };
                }

                if (Linkable)
                {
                    var linkText = new GUITextBlock(new RectTransform(new Point(editingHUD.Rect.Width, heightScaled), isFixedSize: true), TextManager.Get("HoldToLink"), font: GUI.SmallFont);
                    var itemsText = new GUITextBlock(new RectTransform(new Point(editingHUD.Rect.Width, heightScaled), isFixedSize: true), TextManager.Get("AllowedLinks"), font: GUI.SmallFont);
                    string allowedItems = AllowedLinks.None() ?  TextManager.Get("None") :string.Join(", ", AllowedLinks);
                    itemsText.Text = TextManager.AddPunctuation(':', itemsText.Text, allowedItems);
                    itemEditor.AddCustomContent(linkText, 1);
                    itemEditor.AddCustomContent(itemsText, 2);
                    linkText.TextColor = GUI.Style.Orange;
                    itemsText.TextColor = GUI.Style.Orange;
                }
                var buttonContainer = new GUILayoutGroup(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f,
                    CanBeFocused = true
                };
                new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityX"), style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get("MirrorEntityXToolTip"),
                    OnClicked = (button, data) =>
                    {
                        FlipX(relativeToSub: false);
                        return true;
                    }
                };
                new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("MirrorEntityY"), style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get("MirrorEntityYToolTip"),
                    OnClicked = (button, data) =>
                    {
                        FlipY(relativeToSub: false);
                        return true;
                    }
                };
                if (Sprite != null)
                {
                    var reloadTextureButton = new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ReloadSprite"), style: "GUIButtonSmall");
                    reloadTextureButton.OnClicked += (button, data) =>
                    {
                        Sprite.ReloadXML();
                        Sprite.ReloadTexture();
                        return true;
                    };
                }
                new GUIButton(new RectTransform(new Vector2(0.23f, 1.0f), buttonContainer.RectTransform), TextManager.Get("ResetToPrefab"), style: "GUIButtonSmall")
                {
                    OnClicked = (button, data) =>
                    {
                        Reset();
                        CreateEditingHUD();
                        return true;
                    }
                };
                buttonContainer.RectTransform.MinSize = new Point(0, buttonContainer.RectTransform.Children.Max(c => c.MinSize.Y));
                buttonContainer.RectTransform.IsFixedSize = true;
                itemEditor.AddCustomContent(buttonContainer, itemEditor.ContentCount);
                GUITextBlock.AutoScaleAndNormalize(buttonContainer.Children.Select(b => ((GUIButton)b).TextBlock));
            }

            foreach (ItemComponent ic in components)
            {
                if (inGame)
                {
                    if (!ic.AllowInGameEditing) { continue; }
                    if (SerializableProperty.GetProperties<InGameEditable>(ic).Count == 0) { continue; }
                }
                else
                {
                    if (ic.requiredItems.Count == 0 && ic.DisabledRequiredItems.Count == 0 && SerializableProperty.GetProperties<Editable>(ic).Count == 0) { continue; }
                }

                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), listBox.Content.RectTransform), style: "HorizontalLine");

                var componentEditor = new SerializableEntityEditor(listBox.Content.RectTransform, ic, inGame, showName: !inGame, titleFont: GUI.SubHeadingFont);
                componentEditor.Children.First().Color = Color.Black * 0.7f;

                if (inGame)
                {
                    ic.CreateEditingHUD(componentEditor);
                    componentEditor.Recalculate();
                    continue;
                }

                List<RelatedItem> requiredItems = new List<RelatedItem>();
                foreach (var kvp in ic.requiredItems)
                {
                    foreach (RelatedItem relatedItem in kvp.Value)
                    {
                        requiredItems.Add(relatedItem);
                    }
                }
                requiredItems.AddRange(ic.DisabledRequiredItems);

                foreach (RelatedItem relatedItem in requiredItems)
                {
                    //TODO: add to localization
                    var textBlock = new GUITextBlock(new RectTransform(new Point(listBox.Content.Rect.Width, heightScaled)),
                        relatedItem.Type.ToString() + " required", font: GUI.SmallFont)
                    {
                        Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f)
                    };
                    textBlock.RectTransform.IsFixedSize = true;
                    componentEditor.AddCustomContent(textBlock, 1);

                    GUITextBox namesBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), textBlock.RectTransform, Anchor.CenterRight))
                    {
                        Font = GUI.SmallFont,
                        Text = relatedItem.JoinedIdentifiers,
                        OverflowClip = true
                    };
                    textBlock.RectTransform.Resize(new Point(textBlock.Rect.Width, namesBox.RectTransform.MinSize.Y));

                    namesBox.OnDeselected += (textBox, key) =>
                    {
                        relatedItem.JoinedIdentifiers = textBox.Text;
                        textBox.Text = relatedItem.JoinedIdentifiers;
                    };

                    namesBox.OnEnterPressed += (textBox, text) =>
                    {
                        relatedItem.JoinedIdentifiers = text;
                        textBox.Text = relatedItem.JoinedIdentifiers;
                        return true;
                    };
                }                

                ic.CreateEditingHUD(componentEditor);
                componentEditor.Recalculate();
            }

            PositionEditingHUD();
            SetHUDLayout();

            return editingHUD;
        }

        private void CreateTagPicker(GUITextBox textBox, IEnumerable<string> availableTags)
        {
            var msgBox = new GUIMessageBox("", "", new string[] { TextManager.Get("Cancel") }, new Vector2(0.2f, 0.5f), new Point(300, 400));
            msgBox.Buttons[0].OnClicked = msgBox.Close;

            var textList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.8f), msgBox.Content.RectTransform, Anchor.TopCenter))
            {
                OnSelected = (component, userData) =>
                {
                    string text = userData as string ?? "";
                    AddTag(text);
                    textBox.Text = Tags;
                    msgBox.Close();
                    return true;
                }
            };

            foreach (string availableTag in availableTags.ToList().OrderBy(t => t))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), textList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    ToolBox.LimitString(availableTag, GUI.Font, textList.Content.Rect.Width))
                {
                    UserData = availableTag
                };
            }
        }

        /// <summary>
        /// Reposition currently active item interfaces to make sure they don't overlap with each other
        /// </summary>
        private void SetHUDLayout()
        {
            //reset positions first
            List<GUIComponent> elementsToMove = new List<GUIComponent>();

            if (editingHUD != null && editingHUD.UserData == this && 
                ((HasInGameEditableProperties && Character.Controlled?.SelectedConstruction == this) || Screen.Selected == GameMain.SubEditorScreen))
            {
                elementsToMove.Add(editingHUD);
            }

            debugInitialHudPositions.Clear();
            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.GuiFrame == null || ic.AllowUIOverlap || ic.GetLinkUIToComponent() != null) { continue; }
                //if the frame covers nearly all of the screen, don't trying to prevent overlaps because it'd fail anyway
                if (ic.GuiFrame.Rect.Width >= GameMain.GraphicsWidth * 0.9f && ic.GuiFrame.Rect.Height >= GameMain.GraphicsHeight * 0.9f) { continue; }
                ic.GuiFrame.RectTransform.ScreenSpaceOffset = Point.Zero;
                elementsToMove.Add(ic.GuiFrame);
                debugInitialHudPositions.Add(ic.GuiFrame.Rect);
            }

            List<Rectangle> disallowedAreas = new List<Rectangle>();
            if (GameMain.GameSession?.CrewManager != null && Screen.Selected == GameMain.GameScreen)
            {
                int disallowedPadding = (int)(50 * GUI.Scale);
                disallowedAreas.Add(GameMain.GameSession.CrewManager.GetActiveCrewArea());
                disallowedAreas.Add(new Rectangle(
                    HUDLayoutSettings.ChatBoxArea.X - disallowedPadding, HUDLayoutSettings.ChatBoxArea.Y, 
                    HUDLayoutSettings.ChatBoxArea.Width + disallowedPadding, HUDLayoutSettings.ChatBoxArea.Height));                
            }

            if (Screen.Selected is SubEditorScreen editor)
            {
                disallowedAreas.Add(editor.EntityMenu.Rect);
                disallowedAreas.Add(editor.TopPanel.Rect);
                disallowedAreas.Add(editor.ToggleEntityMenuButton.Rect);
            }

            GUI.PreventElementOverlap(elementsToMove, disallowedAreas,
                new Rectangle(
                    0, 20, 
                    GameMain.GraphicsWidth, 
                    HUDLayoutSettings.InventoryTopY > 0 ? HUDLayoutSettings.InventoryTopY - 40 : GameMain.GraphicsHeight - 80));

            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.GuiFrame == null) { continue; }


                var linkUIToComponent = ic.GetLinkUIToComponent();
                if (linkUIToComponent == null) { continue; }                

                ic.GuiFrame.RectTransform.ScreenSpaceOffset = linkUIToComponent.GuiFrame.RectTransform.ScreenSpaceOffset;
            }
        }

        private readonly List<Rectangle> debugInitialHudPositions = new List<Rectangle>();

        public void UpdateHUD(Camera cam, Character character, float deltaTime)
        {
            bool editingHUDCreated = false;
            if ((HasInGameEditableProperties && character.SelectedConstruction == this) ||
                Screen.Selected == GameMain.SubEditorScreen)
            {
                GUIComponent prevEditingHUD = editingHUD;
                UpdateEditing(cam);
                editingHUDCreated = editingHUD != null && editingHUD != prevEditingHUD;
            }

            if (editingHUD == null ||
                !(GUI.KeyboardDispatcher.Subscriber is GUITextBox textBox) ||
                !editingHUD.IsParentOf(textBox))
            {
                editingHUDRefreshTimer -= deltaTime;
            }

            List<ItemComponent> prevActiveHUDs = new List<ItemComponent>(activeHUDs);
            List<ItemComponent> activeComponents = new List<ItemComponent>(components);
            foreach (MapEntity entity in linkedTo)
            {
                if (prefab.IsLinkAllowed(entity.prefab) && entity is Item i)
                {
                    if (!i.DisplaySideBySideWhenLinked) continue;
                    activeComponents.AddRange(i.components);
                }
            }

            activeHUDs.Clear();
            //the HUD of the component with the highest priority will be drawn
            //if all components have a priority of 0, all of them are drawn
            List<ItemComponent> maxPriorityHUDs = new List<ItemComponent>();
            foreach (ItemComponent ic in activeComponents)
            {
                if (ic.HudPriority > 0 && ic.ShouldDrawHUD(character) &&
                    (ic.CanBeSelected || (character.HasEquippedItem(this) && ic.DrawHudWhenEquipped)) &&
                    (maxPriorityHUDs.Count == 0 || ic.HudPriority >= maxPriorityHUDs[0].HudPriority))
                {
                    if (maxPriorityHUDs.Count > 0 && ic.HudPriority > maxPriorityHUDs[0].HudPriority) { maxPriorityHUDs.Clear(); }
                    maxPriorityHUDs.Add(ic);
                }
            }

            if (maxPriorityHUDs.Count > 0)
            {
                activeHUDs.AddRange(maxPriorityHUDs);
            }
            else
            {
                foreach (ItemComponent ic in activeComponents)
                {
                    if (ic.ShouldDrawHUD(character) &&
                        ((ic.CanBeSelected || (character.HasEquippedItem(this) && ic.DrawHudWhenEquipped )) || ic.IsShowingConfigurationUI))
                    {
                        activeHUDs.Add(ic);
                    }
                }
            }

            //active HUDs have changed, need to reposition
            if (!prevActiveHUDs.SequenceEqual(activeHUDs) || editingHUDCreated)
            {
                SetHUDLayout();
            }

            Rectangle mergedHUDRect = Rectangle.Empty;
            foreach (ItemComponent ic in activeHUDs)
            {
                ic.UpdateHUD(character, deltaTime, cam);
                if (ic.GuiFrame != null && ic.GuiFrame.Rect.Height < GameMain.GraphicsHeight)
                {
                    mergedHUDRect = mergedHUDRect == Rectangle.Empty ?
                        ic.GuiFrame.Rect :
                        Rectangle.Union(mergedHUDRect, ic.GuiFrame.Rect);
                }
            }

            if (mergedHUDRect != Rectangle.Empty)
            {
                if (itemInUseWarning != null) { itemInUseWarning.Visible = false; }
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (otherCharacter != character &&
                        otherCharacter.SelectedConstruction == character.SelectedConstruction)
                    {
                        ItemInUseWarning.Visible = true;
                        if (mergedHUDRect.Width > GameMain.GraphicsWidth / 2) { mergedHUDRect.Inflate(-GameMain.GraphicsWidth / 4, 0); }
                        itemInUseWarning.RectTransform.ScreenSpaceOffset = new Point(mergedHUDRect.X, mergedHUDRect.Bottom);
                        itemInUseWarning.RectTransform.NonScaledSize = new Point(mergedHUDRect.Width, (int)(50 * GUI.Scale));
                        if (itemInUseWarning.UserData != otherCharacter)
                        {
                            itemInUseWarning.Text = TextManager.GetWithVariable("ItemInUse", "[character]", otherCharacter.Name);
                            itemInUseWarning.UserData = otherCharacter;
                        }
                        break;
                    }
                }
            }
        }
        
        public void DrawHUD(SpriteBatch spriteBatch, Camera cam, Character character)
        {
            if (HasInGameEditableProperties)
            {
                DrawEditing(spriteBatch, cam);
            }
            
            foreach (ItemComponent ic in activeHUDs)
            {
                if (ic.CanBeSelected)
                {
                    ic.DrawHUD(spriteBatch, character);
                }
            }

            if (GameMain.DebugDraw)
            {
                int i = 0;
                foreach (ItemComponent ic in activeHUDs)
                {
                    if (i >= debugInitialHudPositions.Count) { break; }
                    if (activeHUDs[i].GuiFrame == null) { continue; }
                    if (ic.GuiFrame == null || ic.AllowUIOverlap || ic.GetLinkUIToComponent() != null) { continue; }

                    GUI.DrawRectangle(spriteBatch, debugInitialHudPositions[i], Color.Orange);
                    GUI.DrawRectangle(spriteBatch, ic.GuiFrame.Rect, Color.LightGreen);
                    GUI.DrawLine(spriteBatch, debugInitialHudPositions[i].Location.ToVector2(), ic.GuiFrame.Rect.Location.ToVector2(), Color.Orange);
               
                    i++;
                }            
            }
        }

        readonly List<ColoredText> texts = new List<ColoredText>();
        public List<ColoredText> GetHUDTexts(Character character, bool recreateHudTexts = true)
        {
            // Always create the texts if they have not yet been created
            if (texts.Any() && !recreateHudTexts) { return texts; }
            texts.Clear();
            foreach (ItemComponent ic in components)
            {
                if (string.IsNullOrEmpty(ic.DisplayMsg)) { continue; }
                if (!ic.CanBePicked && !ic.CanBeSelected) { continue; }
                if (ic is Holdable holdable && !holdable.CanBeDeattached()) { continue; }

                Color color = Color.Gray;
                if (ic.HasRequiredItems(character, false))
                {
                    if (ic is Repairable)
                    {
                        if (!IsFullCondition) { color = Color.Cyan; }
                    }
                    else
                    {
                        color = Color.Cyan;
                    }
                }
                texts.Add(new ColoredText(ic.DisplayMsg, color, false));
            }
            if ((PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift)) && CrewManager.DoesItemHaveContextualOrders(this))
            {
                texts.Add(new ColoredText(TextManager.ParseInputTypes(TextManager.Get("itemmsgcontextualorders")), Color.Cyan, false));
            }
            return texts;
        }

        public override void AddToGUIUpdateList()
        {
            if (Screen.Selected is SubEditorScreen)
            {
                if (editingHUD != null && editingHUD.UserData == this) { editingHUD.AddToGUIUpdateList(); }
            }
            else
            {
                if (HasInGameEditableProperties)
                {
                    if (editingHUD != null && editingHUD.UserData == this) { editingHUD.AddToGUIUpdateList(); }
                }
            }

            if (Character.Controlled != null && Character.Controlled?.SelectedConstruction != this) { return; }

            bool needsLayoutUpdate = false;
            foreach (ItemComponent ic in activeHUDs)
            {
                if (!ic.CanBeSelected) { continue; }

                bool useAlternativeLayout = activeHUDs.Count > 1;
                bool wasUsingAlternativeLayout = ic.UseAlternativeLayout;
                ic.UseAlternativeLayout = useAlternativeLayout;
                needsLayoutUpdate |= ic.UseAlternativeLayout != wasUsingAlternativeLayout;
                ic.AddToGUIUpdateList();
            }

            if (itemInUseWarning != null && itemInUseWarning.Visible)
            {
                itemInUseWarning.AddToGUIUpdateList();
            }

            if (needsLayoutUpdate)
            {
                SetHUDLayout();
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (type == ServerNetObject.ENTITY_POSITION)
            {
                ClientReadPosition(type, msg, sendingTime);
                return;
            }

            NetEntityEvent.Type eventType =
                (NetEntityEvent.Type)msg.ReadRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);
            
            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    {
                        int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                        if (components[componentIndex] is IServerSerializable serverSerializable)
                        {
                            serverSerializable.ClientRead(type, msg, sendingTime);
                        }
                        else
                        {
                            throw new Exception("Failed to read component state - " + components[componentIndex].GetType() + " is not IServerSerializable.");
                        }
                    }
                    break;
                case NetEntityEvent.Type.InventoryState:
                    {
                        int containerIndex = msg.ReadRangedInteger(0, components.Count - 1);
                        if (components[containerIndex] is ItemContainer container)
                        {
                            container.Inventory.ClientRead(type, msg, sendingTime);
                        }
                        else
                        {
                            throw new Exception("Failed to read inventory state - " + components[containerIndex].GetType() + " is not an ItemContainer.");
                        }
                    }
                    break;
                case NetEntityEvent.Type.Status:
                    float prevCondition = condition;
                    condition = msg.ReadSingle();
                    if (prevCondition > 0.0f && condition <= 0.0f)
                    {
                        ApplyStatusEffects(ActionType.OnBroken, 1.0f);
                        foreach (ItemComponent ic in components)
                        {
                            ic.PlaySound(ActionType.OnBroken);
                        }
                    }
                    SetActiveSprite();
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    {
                        ActionType actionType = (ActionType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length - 1);
                        byte componentIndex         = msg.ReadByte();
                        ushort targetCharacterID    = msg.ReadUInt16();
                        byte targetLimbID           = msg.ReadByte();
                        ushort useTargetID          = msg.ReadUInt16();
                        Vector2? worldPosition      = null;
                        bool hasPosition            = msg.ReadBoolean();
                        if (hasPosition)
                        {
                            worldPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                        }

                        ItemComponent targetComponent = componentIndex < components.Count ? components[componentIndex] : null;
                        Character targetCharacter = FindEntityByID(targetCharacterID) as Character;
                        Limb targetLimb = targetCharacter != null && targetLimbID < targetCharacter.AnimController.Limbs.Length ? 
                            targetCharacter.AnimController.Limbs[targetLimbID] : null;
                        Entity useTarget = FindEntityByID(useTargetID);

                        if (targetComponent == null)
                        {
                            ApplyStatusEffects(actionType, 1.0f, targetCharacter, targetLimb, useTarget, true, worldPosition: worldPosition);
                        }
                        else
                        {
                            targetComponent.ApplyStatusEffects(actionType, 1.0f, targetCharacter, targetLimb, useTarget, worldPosition: worldPosition);
                        }                        
                    }
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    ReadPropertyChange(msg, false);
                    editingHUDRefreshPending = true;
                    break;
                case NetEntityEvent.Type.Invalid:
                    break;
            }
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            if (extraData == null || extraData.Length == 0 || !(extraData[0] is NetEntityEvent.Type))
            {
                return;
            }

            NetEntityEvent.Type eventType = (NetEntityEvent.Type)extraData[0];
            msg.WriteRangedInteger((int)eventType, 0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);
            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    int componentIndex = (int)extraData[1];
                    msg.WriteRangedInteger(componentIndex, 0, components.Count - 1);
                    (components[componentIndex] as IClientSerializable).ClientWrite(msg, extraData);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    int containerIndex = (int)extraData[1];
                    msg.WriteRangedInteger(containerIndex, 0, components.Count - 1);
                    (components[containerIndex] as ItemContainer).Inventory.ClientWrite(msg, extraData);
                    break;
                case NetEntityEvent.Type.Treatment:
                    UInt16 characterID = (UInt16)extraData[1];
                    Limb targetLimb = (Limb)extraData[2];

                    Character targetCharacter = FindEntityByID(characterID) as Character;

                    msg.Write(characterID);
                    msg.Write(targetCharacter == null ? (byte)255 : (byte)Array.IndexOf(targetCharacter.AnimController.Limbs, targetLimb));               
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    WritePropertyChange(msg, extraData, true);
                    editingHUDRefreshTimer = 1.0f;
                    break;
                case NetEntityEvent.Type.Combine:
                    UInt16 combineTargetID = (UInt16)extraData[1];
                    msg.Write(combineTargetID);
                    break;
            }
            msg.WritePadBits();
        }

        partial void UpdateNetPosition(float deltaTime)
        {
            if (GameMain.Client == null) { return; }

            if (parentInventory != null || body == null || !body.Enabled || Removed)
            {
                positionBuffer.Clear();
                return;
            }

            isActive = true;

            Vector2 newVelocity = body.LinearVelocity;
            Vector2 newPosition = body.SimPosition;
            float newAngularVelocity = body.AngularVelocity;
            float newRotation = body.Rotation;
            body.CorrectPosition(positionBuffer, out newPosition, out newVelocity, out newRotation, out newAngularVelocity);

            body.LinearVelocity = newVelocity;
            body.AngularVelocity = newAngularVelocity;
            if (Vector2.DistanceSquared(newPosition, body.SimPosition) > 0.0001f ||
                Math.Abs(newRotation - body.Rotation) > 0.01f)
            {
                body.TargetPosition = newPosition;
                body.TargetRotation = newRotation;
                body.MoveToTargetPosition(lerp: true);
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
        }

        public void ClientReadPosition(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (body == null)
            {
                string errorMsg = "Received a position update for an item with no physics body (" + Name + ")";
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
                GameAnalyticsManager.AddErrorEventOnce("Item.ClientReadPosition:nophysicsbody", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            var posInfo = body.ClientRead(type, msg, sendingTime, parentDebugName: Name);
            msg.ReadPadBits();
            if (posInfo != null)
            {
                int index = 0;
                while (index < positionBuffer.Count && sendingTime > positionBuffer[index].Timestamp)
                {
                    index++;
                }

                positionBuffer.Insert(index, posInfo);
            }
            /*body.FarseerBody.Awake = awake;
            if (body.FarseerBody.Awake)
            {
                if ((newVelocity - body.LinearVelocity).LengthSquared() > 8.0f * 8.0f) body.LinearVelocity = newVelocity;
            }
            else
            {
                try
                {
                    body.FarseerBody.Enabled = false;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Exception in PhysicsBody.Enabled = false (" + body.PhysEnabled + ")", e);
                    if (body.UserData != null) DebugConsole.NewMessage("PhysicsBody UserData: " + body.UserData.GetType().ToString(), GUI.Style.Red);
                    if (GameMain.World.ContactManager == null) DebugConsole.NewMessage("ContactManager is null!", GUI.Style.Red);
                    else if (GameMain.World.ContactManager.BroadPhase == null) DebugConsole.NewMessage("Broadphase is null!", GUI.Style.Red);
                    if (body.FarseerBody.FixtureList == null) DebugConsole.NewMessage("FixtureList is null!", GUI.Style.Red);
                }
            }

            if ((newPosition - SimPosition).Length() > body.LinearVelocity.Length() * 2.0f)
            {
                if (body.SetTransform(newPosition, newRotation))
                {
                    Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
                    rect.X = (int)(displayPos.X - rect.Width / 2.0f);
                    rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
                }
            }*/
        }

        public void CreateClientEvent<T>(T ic) where T : ItemComponent, IClientSerializable
        {
            if (GameMain.Client == null) return;

            int index = components.IndexOf(ic);
            if (index == -1) return;

            GameMain.Client.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ComponentState, index });
        }

        public void CreateClientEvent<T>(T ic, object[] extraData) where T : ItemComponent, IClientSerializable
        {
            if (GameMain.Client == null) return;

            int index = components.IndexOf(ic);
            if (index == -1) return;

            object[] data = new object[] { NetEntityEvent.Type.ComponentState, index }.Concat(extraData).ToArray();
            GameMain.Client.CreateEntityEvent(this, data);
        }

        public static Item ReadSpawnData(IReadMessage msg, bool spawn = true)
        {
            string itemName = msg.ReadString();
            string itemIdentifier = msg.ReadString();
            bool descriptionChanged = msg.ReadBoolean();
            string itemDesc = "";
            if (descriptionChanged)
            {
                itemDesc = msg.ReadString();
            }
            ushort itemId = msg.ReadUInt16();
            ushort inventoryId = msg.ReadUInt16();

            DebugConsole.Log("Received entity spawn message for item " + itemName + ".");

            Vector2 pos = Vector2.Zero;
            Submarine sub = null;
            int itemContainerIndex = -1;
            int inventorySlotIndex = -1;

            if (inventoryId > 0)
            {
                itemContainerIndex = msg.ReadByte();
                inventorySlotIndex = msg.ReadByte();
            }
            else
            {
                pos = new Vector2(msg.ReadSingle(), msg.ReadSingle());

                ushort subID = msg.ReadUInt16();
                if (subID > 0)
                {
                    sub = Submarine.Loaded.Find(s => s.ID == subID);
                }
            }

            byte bodyType = msg.ReadByte();

            byte teamID = msg.ReadByte();
            bool tagsChanged = msg.ReadBoolean();
            string tags = "";
            if (tagsChanged)
            {
                tags = msg.ReadString();
            }

            if (!spawn) return null;

            //----------------------------------------

            var itemPrefab = string.IsNullOrEmpty(itemIdentifier) ?
                MapEntityPrefab.Find(itemName, null, showErrorMessages: false) as ItemPrefab :
                MapEntityPrefab.Find(itemName, itemIdentifier, showErrorMessages: false) as ItemPrefab;
            if (itemPrefab == null)
            {
                string errorMsg = "Failed to spawn item, prefab not found (name: " + (itemName ?? "null") + ", identifier: " + (itemIdentifier ?? "null") + ")";
                errorMsg += "\n" + string.Join(", ", GameMain.Config.SelectedContentPackages.Select(cp => cp.Name));
                GameAnalyticsManager.AddErrorEventOnce("Item.ReadSpawnData:PrefabNotFound" + (itemName ?? "null") + (itemIdentifier ?? "null"),
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Critical,
                    errorMsg);
                DebugConsole.ThrowError(errorMsg);
                return null;
            }

            Inventory inventory = null;
            if (inventoryId > 0)
            {
                var inventoryOwner = FindEntityByID(inventoryId);
                if (inventoryOwner is Character character)
                {
                    inventory = character.Inventory;
                }
                else if (inventoryOwner is Item parentItem)
                {
                    if (itemContainerIndex < 0 || itemContainerIndex >= parentItem.components.Count)
                    {
                        string errorMsg =
                            $"Failed to spawn item \"{(itemIdentifier ?? "null")}\" in the inventory of \"{parentItem.prefab.Identifier} ({parentItem.ID})\" (component index out of range). Index: {itemContainerIndex}, components: {parentItem.components.Count}.";
                        GameAnalyticsManager.AddErrorEventOnce("Item.ReadSpawnData:ContainerIndexOutOfRange" + (itemName ?? "null") + (itemIdentifier ?? "null"),
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            errorMsg);
                        DebugConsole.ThrowError(errorMsg);
                    }
                    else if (parentItem.components[itemContainerIndex] is ItemContainer container)
                    {
                        inventory = container.Inventory;
                    }
                }
            }

            var item = new Item(itemPrefab, pos, sub)
            {
                ID = itemId
            };

            if (item.body != null)
            {
                item.body.BodyType = (BodyType)bodyType;
            }

            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = (Character.TeamType)teamID;
            }
            if (descriptionChanged) { item.Description = itemDesc; }
            if (tagsChanged) { item.Tags = tags; }

            if (sub != null)
            {
                item.CurrentHull = Hull.FindHull(pos + sub.Position, null, true);
                item.Submarine = item.CurrentHull?.Submarine;
            }

            if (inventory != null)
            {
                if (inventorySlotIndex >= 0 && inventorySlotIndex < 255 &&
                    inventory.TryPutItem(item, inventorySlotIndex, false, false, null, false))
                {
                    return item;
                }
                inventory.TryPutItem(item, null, item.AllowedSlots, false);
            }

            return item;
        }

        partial void RemoveProjSpecific()
        {
            if (Inventory.draggingItem == this)
            {
                Inventory.draggingItem = null;
                Inventory.draggingSlot = null;
            }
        }
    }
}
