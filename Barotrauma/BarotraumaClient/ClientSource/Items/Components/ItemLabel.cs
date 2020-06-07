﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent
    {
        private GUITextBlock textBlock;

        private Color textColor;

        private float scrollAmount;
        private string scrollingText;
        private float scrollPadding;
        private int scrollIndex;
        private bool needsScrolling;

        private float[] charWidths;

        [Serialize("0,0,0,0", true, description: "The amount of padding around the text in pixels (left,top,right,bottom). ")]
        public Vector4 Padding
        {
            get { return TextBlock.Padding; }
            set { TextBlock.Padding = value; }
        }

        private string text;
        [Serialize("", true, translationTextTag: "Label.", description: "The text displayed in the label.", alwaysUseInstanceValues: true), Editable(100)]
        public string Text
        {
            get { return text; }
            set
            {
                if (value == text || item.Rect.Width < 5) return;

                if (TextBlock.Rect.Width != item.Rect.Width || textBlock.Rect.Height != item.Rect.Height)
                {
                    textBlock = null;
                }
                
                text = value;
                DisplayText = TextManager.Get(text, returnNull: true) ?? value;
                TextBlock.Text = DisplayText;
                if (Screen.Selected == GameMain.SubEditorScreen && Scrollable)
                {
                    TextBlock.Text = ToolBox.LimitString(DisplayText, textBlock.Font, item.Rect.Width);
                }
                SetScrollingText();
            }
        }

        public string DisplayText
        {
            get;
            private set;
        }

        [Editable, Serialize("0,0,0,255", true, description: "The color of the text displayed on the label (R,G,B,A).", alwaysUseInstanceValues: true)]
        public Color TextColor
        {
            get { return textColor; }
            set
            {
                if (textBlock != null) textBlock.TextColor = value;
                textColor = value;
            }
        }

        [Editable(0.0f, 10.0f), Serialize(1.0f, true, description: "The scale of the text displayed on the label.", alwaysUseInstanceValues: true)]
        public float TextScale
        {
            get { return textBlock == null ? 1.0f : textBlock.TextScale; }
            set
            {
                if (textBlock != null) textBlock.TextScale = MathHelper.Clamp(value, 0.1f, 10.0f);
            }
        }

        private bool scrollable;
        [Serialize(false, true, description: "Should the text scroll horizontally across the item if it's too long to be displayed all at once.")]
        public bool Scrollable
        {
            get { return scrollable; }
            set
            {
                scrollable = value;
                IsActive = value;
                TextBlock.Wrap = !scrollable;
                TextBlock.TextAlignment = scrollable ? Alignment.CenterLeft : Alignment.Center;
            }
        }

        [Serialize(20.0f, true, description: "How fast the text scrolls across the item (only valid if Scrollable is set to true).")]
        public float ScrollSpeed
        {
            get;
            set;
        }

        private GUITextBlock TextBlock
        {
            get
            {
                if (textBlock == null)
                {
                    textBlock = new GUITextBlock(new RectTransform(item.Rect.Size), "",
                        textColor: textColor, font: GUI.UnscaledSmallFont, textAlignment: Alignment.Center, wrap: true, style: null)
                    {
                        TextDepth = item.SpriteDepth - 0.00001f,
                        RoundToNearestPixel = false,
                        TextScale = TextScale
                    };
                }
                return textBlock;
            }
        }

        public ItemLabel(Item item, XElement element)
            : base(item, element)
        {            
        }

        private void SetScrollingText()
        {
            if (!scrollable) return;

            float totalWidth = textBlock.Font.MeasureString(DisplayText).X;
            float textAreaWidth = Math.Max(textBlock.Rect.Width - textBlock.Padding.X - textBlock.Padding.Z, 0);
            if (totalWidth >= textAreaWidth)
            {
                //add enough spaces to fill the rect
                //(so the text can scroll entirely out of view before we reset it back to start)
                needsScrolling = true;
                float spaceWidth = textBlock.Font.MeasureChar(' ').X;
                scrollingText = new string(' ', (int)Math.Ceiling(textAreaWidth / spaceWidth)) + DisplayText;
            }
            else
            {
                //whole text can fit in the textblock, no need to scroll
                needsScrolling = false;
                scrollingText = DisplayText;
                scrollAmount = 0.0f;
                scrollIndex = 0;
                return;
            }

            //calculate character widths
            scrollPadding = 0;
            charWidths = new float[scrollingText.Length];
            for (int i = 0; i < scrollingText.Length; i++)
            {
                float charWidth = TextBlock.Font.MeasureChar(scrollingText[i]).X;
                scrollPadding = Math.Max(charWidth, scrollPadding);
                charWidths[i] = charWidth;
            }

            scrollIndex = MathHelper.Clamp(scrollIndex, 0, DisplayText.Length);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (!scrollable) return;

            if (scrollingText == null)
            {
                SetScrollingText();
            }

            if (!needsScrolling) return;

            scrollAmount -= deltaTime * ScrollSpeed;

            float currLength = 0;
            StringBuilder sb = new StringBuilder();
            float textAreaWidth = textBlock.Rect.Width - textBlock.Padding.X - textBlock.Padding.Z;
            for (int i = scrollIndex; i < scrollingText.Length; i++)
            {
                //first character is out of view -> skip to next character
                if (i == scrollIndex && scrollAmount < -charWidths[i])
                {
                    scrollIndex++;
                    scrollAmount = 0;
                    if (scrollIndex >= scrollingText.Length) //reached the last character, reset
                    {
                        scrollIndex = 0;
                        break;
                    }
                    continue;
                }

                //reached the right edge, stop adding more character
                if (scrollAmount + (currLength + charWidths[i] + scrollPadding) >= textAreaWidth)
                {
                    break;
                }
                else
                {
                    currLength += charWidths[i];
                    sb.Append(scrollingText[i]);
                }
            }

            TextBlock.Text = sb.ToString();            
        }
                
        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            var drawPos = new Vector2(
                item.DrawPosition.X - item.Rect.Width / 2.0f,
                -(item.DrawPosition.Y + item.Rect.Height / 2.0f));

            Rectangle worldRect = item.WorldRect;
            if (worldRect.X > Screen.Selected.Cam.WorldView.Right ||
                worldRect.Right < Screen.Selected.Cam.WorldView.X ||
                worldRect.Y < Screen.Selected.Cam.WorldView.Y - Screen.Selected.Cam.WorldView.Height ||
                worldRect.Y - worldRect.Height > Screen.Selected.Cam.WorldView.Y)
            {
                return;
            }

            textBlock.TextDepth = item.SpriteDepth - 0.0001f;
            textBlock.TextOffset = drawPos - textBlock.Rect.Location.ToVector2() + new Vector2(scrollAmount + scrollPadding, 0.0f);
            textBlock.DrawManually(spriteBatch);
        }
    }
}
