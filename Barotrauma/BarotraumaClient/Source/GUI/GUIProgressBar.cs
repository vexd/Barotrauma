﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIProgressBar : GUIComponent
    {
        private bool isHorizontal;

        private GUIFrame frame, slider;
        private float barSize;
                
        public delegate float ProgressGetterHandler();
        public ProgressGetterHandler ProgressGetter;

        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set { isHorizontal = value; }
        }

        public float BarSize
        {
            get { return barSize; }
            set
            {
                barSize = MathHelper.Clamp(value, 0.0f, 1.0f);
                UpdateRect();
            }
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIProgressBar(Rectangle rect, Color color, float barSize, GUIComponent parent = null)
            : this(rect, color, barSize, (Alignment.Left | Alignment.Top), parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIProgressBar(Rectangle rect, Color color, float barSize, Alignment alignment, GUIComponent parent = null)
            : this(rect, color, null, barSize, alignment, parent)
        {

        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIProgressBar(Rectangle rect, Color color, string style, float barSize, Alignment alignment, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            this.color = color;
            isHorizontal = (rect.Width > rect.Height);

            this.alignment = alignment;
            
            if (parent != null)
                parent.AddChild(this);

            frame = new GUIFrame(new Rectangle(0, 0, 0, 0), null, this);
            GUI.Style.Apply(frame, "", this);

            slider = new GUIFrame(new Rectangle(0, 0, 0, 0), null);
            GUI.Style.Apply(slider, "Slider", this);

            this.barSize = barSize;
            UpdateRect();
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIProgressBar(RectTransform rectT, float barSize, Color? color = null, string style = "") : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            isHorizontal = (Rect.Width > Rect.Height);
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(frame, "", this);
            slider = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(slider, "Slider", this);
            this.barSize = barSize;
            UpdateRect();
        }

        /*public override void ApplyStyle(GUIComponentStyle style)
        {
            if (frame == null) return;

            frame.Color = style.Color;
            frame.HoverColor = style.HoverColor;
            frame.SelectedColor = style.SelectedColor;

            Padding = style.Padding;

            frame.OutlineColor = style.OutlineColor;

            this.style = style;
        }*/

        private void UpdateRect()
        {
            if (RectTransform != null)
            {
                var newSize = isHorizontal ? new Vector2(barSize, 1) : new Vector2(1, barSize);
                slider.RectTransform.Resize(newSize);
            }
            else
            {
                slider.Rect = new Rectangle(
                    (int)(frame.Rect.X + padding.X),
                    (int)(frame.Rect.Y + padding.Y),
                    isHorizontal ? (int)((frame.Rect.Width - padding.X - padding.Z) * barSize) : frame.Rect.Width,
                    isHorizontal ? (int)(frame.Rect.Height - padding.Y - padding.W) : (int)(frame.Rect.Height * barSize));
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            if (ProgressGetter != null) BarSize = ProgressGetter();   

            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (slider.sprites != null && slider.sprites[state].Count > 0)
            {
                foreach (UISprite uiSprite in slider.sprites[state])
                {
                    if (uiSprite.Tile)
                    {
                        uiSprite.Sprite.DrawTiled(spriteBatch, slider.Rect.Location.ToVector2(), slider.Rect.Size.ToVector2(), color: currColor);
                    }
                    else if (uiSprite.Slice)
                    {
                        Vector2 pos = new Vector2(slider.Rect.X, slider.Rect.Y);

                        int centerWidth = System.Math.Max(slider.Rect.Width - uiSprite.Slices[0].Width - uiSprite.Slices[2].Width, 0);
                        int centerHeight = System.Math.Max(slider.Rect.Height - uiSprite.Slices[0].Height - uiSprite.Slices[8].Height, 0);

                        Vector2 scale = new Vector2(
                            MathHelper.Clamp((float)slider.Rect.Width / (uiSprite.Slices[0].Width + uiSprite.Slices[2].Width), 0, 1),
                            MathHelper.Clamp((float)slider.Rect.Height / (uiSprite.Slices[0].Height + uiSprite.Slices[6].Height), 0, 1));

                        for (int x = 0; x < 3; x++)
                        {
                            float width = (x == 1 ? centerWidth : uiSprite.Slices[x].Width) * scale.X;
                            for (int y = 0; y < 3; y++)
                            {
                                float height = (y == 1 ? centerHeight : uiSprite.Slices[x + y * 3].Height) * scale.Y;

                                spriteBatch.Draw(uiSprite.Sprite.Texture,
                                    new Rectangle((int)pos.X, (int)pos.Y, (int)width, (int)height),
                                    uiSprite.Slices[x + y * 3],
                                    currColor * (currColor.A / 255.0f));
                                
                                pos.Y += height;
                            }
                            pos.X += width;
                            pos.Y = slider.Rect.Y;
                        }                        
                    }
                    else
                    {
                        spriteBatch.Draw(uiSprite.Sprite.Texture,
                            slider.Rect, new Rectangle(
                                uiSprite.Sprite.SourceRect.X, 
                                uiSprite.Sprite.SourceRect.Y, 
                                (int)(uiSprite.Sprite.SourceRect.Width * (isHorizontal ? barSize : 1.0f)),
                                (int)(uiSprite.Sprite.SourceRect.Height * (isHorizontal ? 1.0f : barSize))), 
                            currColor);
                    }
                }
            }
        }

    }
}
