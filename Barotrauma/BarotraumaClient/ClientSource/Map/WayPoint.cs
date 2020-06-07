﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class WayPoint : MapEntity
    {
        private static Dictionary<string, Sprite> iconSprites;
        private const int WaypointSize = 12, SpawnPointSize = 32;

        public override bool IsVisible(Rectangle worldView)
        {
            return Screen.Selected == GameMain.SubEditorScreen || GameMain.DebugDraw;
        }

        public override bool SelectableInEditor
        {
            get { return !IsHidden(); }
        }


        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing && (!GameMain.DebugDraw || Screen.Selected.Cam.Zoom < 0.1f)) { return; }
            if (IsHidden()) { return; }

            Vector2 drawPos = Position;
            if (Submarine != null) { drawPos += Submarine.DrawPosition; }
            drawPos.Y = -drawPos.Y;

            Draw(spriteBatch, drawPos);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 drawPos)
        {
            Color clr = currentHull == null ? Color.CadetBlue : GUI.Style.Green;
            if (spawnType != SpawnType.Path) { clr = Color.Gray; }
            if (isObstructed)
            {
                clr = Color.Black;
            }
            if (IsHighlighted || IsHighlighted) { clr = Color.Lerp(clr, Color.White, 0.8f); }

            int iconSize = spawnType == SpawnType.Path ? WaypointSize : SpawnPointSize;
            if (ConnectedGap != null || Ladders != null || Stairs != null || SpawnType != SpawnType.Path) { iconSize = (int)(iconSize * 1.5f); }

            if (IsSelected || IsHighlighted)
            {
                int glowSize = (int)(iconSize * 1.5f);
                GUI.Style.UIGlowCircular.Draw(spriteBatch,
                    new Rectangle((int)(drawPos.X - glowSize / 2), (int)(drawPos.Y - glowSize / 2), glowSize, glowSize),
                    Color.White);
            }

            Sprite sprite = iconSprites[SpawnType.ToString()];
            if (spawnType == SpawnType.Human && AssignedJob?.Icon != null)
            {
                sprite = iconSprites["Path"];
            }
            else if (ConnectedDoor != null)
            {
                sprite = iconSprites["Door"];
            }
            else if (Ladders != null)
            {
                sprite = iconSprites["Ladder"];
            }
            sprite.Draw(spriteBatch, drawPos, clr, scale: iconSize / (float)sprite.SourceRect.Width, depth: 0.001f);
            sprite.RelativeOrigin = Vector2.One * 0.5f;
            if (spawnType == SpawnType.Human && AssignedJob?.Icon != null)
            {
                AssignedJob.Icon.Draw(spriteBatch, drawPos, AssignedJob.UIColor, scale: iconSize / (float)AssignedJob.Icon.SourceRect.Width * 0.8f, depth: 0.0f);
            }

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    drawPos,
                    new Vector2(e.DrawPosition.X, -e.DrawPosition.Y),
                    (isObstructed ? Color.Gray : GUI.Style.Green) * 0.7f, width: 5, depth: 0.002f);
            }

            GUI.SmallFont.DrawString(spriteBatch,
                ID.ToString(),
                new Vector2(DrawPosition.X - 10, -DrawPosition.Y - 30),
                Color.WhiteSmoke);
        }

        public override bool IsMouseOn(Vector2 position)
        {
            if (IsHidden()) { return false; }
            float dist = Vector2.DistanceSquared(position, WorldPosition);
            float radius = (SpawnType == SpawnType.Path ? WaypointSize : SpawnPointSize) * 0.6f;
            return dist < radius * radius;
        }

        private bool IsHidden()
        {
            if (spawnType == SpawnType.Path)
            {
                return (!GameMain.DebugDraw && !ShowWayPoints);
            }
            else
            {
                return (!GameMain.DebugDraw && !ShowSpawnPoints);
            }
        }

        public override void UpdateEditing(Camera cam)
        {
            if (editingHUD == null || editingHUD.UserData != this)
            {
                editingHUD = CreateEditingHUD();
            }

            if (IsSelected && PlayerInput.PrimaryMouseButtonClicked())
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

                if (PlayerInput.KeyDown(Keys.Space))
                {
                    foreach (MapEntity e in mapEntityList)
                    {
                        if (e.GetType() != typeof(WayPoint)) continue;
                        if (e == this) continue;

                        if (!Submarine.RectContains(e.Rect, position)) continue;

                        if (linkedTo.Contains(e))
                        {
                            linkedTo.Remove(e);
                            e.linkedTo.Remove(this);
                        }
                        else
                        {
                            linkedTo.Add(e);
                            e.linkedTo.Add(this);
                        }
                    }
                }
                else
                {
                    // Update gaps, ladders, and stairs
                    UpdateLinkedEntity(position, Gap.GapList, gap => ConnectedGap = gap, gap =>
                    {
                        if (ConnectedGap == gap)
                        {
                            ConnectedGap = null;
                        }
                    });
                    UpdateLinkedEntity(position, Item.ItemList, i =>
                    {
                        var ladder = i?.GetComponent<Ladder>();
                        if (ladder != null)
                        {
                            Ladders = ladder;
                        }
                    }, i =>
                    {
                        var ladder = i?.GetComponent<Ladder>();
                        if (ladder != null)
                        {
                            if (Ladders == ladder)
                            {
                                Ladders = null;
                            }
                        }
                    }, inflate: 5);
                    // TODO: Cannot check the rectangle, since the rectangle is not rotated -> Need to use the collider.
                    //var stairList = mapEntityList.Where(me => me is Structure s && s.StairDirection != Direction.None).Select(me => me as Structure);
                    //UpdateLinkedEntity(position, stairList, s =>
                    //{
                    //    Stairs = s;
                    //}, s =>
                    //{
                    //    if (Stairs == s)
                    //    {
                    //        Stairs = null;
                    //    }
                    //});
                }
            }
        }

        private void UpdateLinkedEntity<T>(Vector2 worldPos, IEnumerable<T> list, Action<T> match, Action<T> noMatch, int inflate = 0) where T : MapEntity
        {
            foreach (var entity in list)
            {
                var rect = entity.WorldRect;
                rect.Inflate(inflate, inflate);
                if (Submarine.RectContains(rect, worldPos))
                {
                    match(entity);
                }
                else
                {
                    noMatch(entity);
                }
            }
        }

        private bool ChangeSpawnType(GUIButton button, object obj)
        {
            GUITextBlock spawnTypeText = button.Parent.GetChildByUserData("spawntypetext") as GUITextBlock;
            spawnType += (int)button.UserData;
            var values = Enum.GetValues(typeof(SpawnType));
            int firstIndex = 1;
            int lastIndex = values.Length - 1;
            if ((int)spawnType > lastIndex)
            {
                spawnType = (SpawnType)firstIndex;
            }
            if ((int)spawnType < firstIndex)
            {
                spawnType = (SpawnType)values.GetValue(lastIndex);
            }
            spawnTypeText.Text = spawnType.ToString();
            return true;
        }

        private bool EnterIDCardDesc(GUITextBox textBox, string text)
        {
            IdCardDesc = text;
            textBox.Text = text;
            textBox.Color = GUI.Style.Green;

            textBox.Deselect();

            return true;
        }
        private bool EnterIDCardTags(GUITextBox textBox, string text)
        {
            IdCardTags = text.Split(',');
            textBox.Text = string.Join(",", IdCardTags);
            textBox.Flash(GUI.Style.Green);
            textBox.Deselect();
            return true;
        }
        
        private bool TextBoxChanged(GUITextBox textBox, string text)
        {
            textBox.Color = GUI.Style.Red;

            return true;
        }

        private GUIComponent CreateEditingHUD(bool inGame = false)
        {
            int width = 500;
            int height = spawnType == SpawnType.Path ? 80 : 200;
            int x = GameMain.GraphicsWidth / 2 - width / 2, y = 30;

            editingHUD = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas) { ScreenSpaceOffset = new Point(x, y) })
            {
                UserData = this
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), editingHUD.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            if (spawnType == SpawnType.Path)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("Waypoint"), font: GUI.LargeFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("LinkWaypoint"));
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), TextManager.Get("Spawnpoint"), font: GUI.LargeFont);
                
                var spawnTypeContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), spawnTypeContainer.RectTransform), TextManager.Get("SpawnType"));

                var button = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), spawnTypeContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton")
                {
                    UserData = -1,
                    OnClicked = ChangeSpawnType
                };
                new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), spawnTypeContainer.RectTransform), spawnType.ToString(), textAlignment: Alignment.Center)
                {
                    UserData = "spawntypetext"
                };
                button = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), spawnTypeContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton")
                {
                    UserData = 1,
                    OnClicked = ChangeSpawnType
                };

                var descText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), 
                    TextManager.Get("IDCardDescription"), font: GUI.SmallFont);
                GUITextBox propertyBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), descText.RectTransform, Anchor.CenterRight), idCardDesc)
                {
                    MaxTextLength = 150,
                    OnEnterPressed = EnterIDCardDesc,
                    ToolTip = TextManager.Get("IDCardDescriptionTooltip")
                };
                propertyBox.OnTextChanged += TextBoxChanged;

                var tagsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("IDCardTags"), font: GUI.SmallFont);
                propertyBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 1.0f), tagsText.RectTransform, Anchor.CenterRight), string.Join(", ", idCardTags))
                {
                    MaxTextLength = 60,
                    OnEnterPressed = EnterIDCardTags,
                    ToolTip = TextManager.Get("IDCardTagsTooltip")
                };
                propertyBox.OnTextChanged += TextBoxChanged;


                var jobsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform),
                    TextManager.Get("SpawnpointJobs"), font: GUI.SmallFont)
                {
                    ToolTip = TextManager.Get("SpawnpointJobsTooltip")
                };
                var jobDropDown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), jobsText.RectTransform, Anchor.CenterRight))
                {
                    ToolTip = TextManager.Get("SpawnpointJobsTooltip"),
                    OnSelected = (selected, userdata) =>
                    {
                        assignedJob = userdata as JobPrefab;
                        return true;
                    }
                };
                jobDropDown.AddItem(TextManager.Get("Any"), null);
                foreach (JobPrefab jobPrefab in JobPrefab.Prefabs)
                {
                    jobDropDown.AddItem(jobPrefab.Name, jobPrefab);
                }
                jobDropDown.SelectItem(assignedJob);
            }
            
            PositionEditingHUD();

            return editingHUD;
        }        
    }
}
