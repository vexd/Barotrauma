﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class GUIListBox : GUIComponent
    {
        protected List<GUIComponent> selected;

        public delegate bool OnSelectedHandler(GUIComponent component, object obj);
        public OnSelectedHandler OnSelected;

        public delegate object CheckSelectedHandler();
        public CheckSelectedHandler CheckSelected;

        private GUIScrollBar scrollBar;
        private GUIFrame frame;

        private int totalSize;

        private int spacing;

        private bool scrollBarEnabled;
        private bool scrollBarHidden;
        private bool hasBeenResized;

        private bool enabled;

        public bool SelectMultiple;

        public bool HideChildrenOutsideFrame = true;

        public GUIComponent Selected
        {
            get
            {
                return selected.FirstOrDefault();
            }
        }

        public List<GUIComponent> AllSelected
        {
            get { return selected; }
        }

        public object SelectedData
        {
            get
            {
                return Selected?.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (Selected == null) return -1;
                return Children.FindIndex(x => x == Selected);
            }
        }

        public float BarScroll
        {
            get { return scrollBar.BarScroll; }
            set { scrollBar.BarScroll = value; }
        }

        public float BarSize
        {
            get { return scrollBar.BarSize; }
        }

        public int Spacing
        {
            get { return spacing; }
            set { spacing = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                //scrollBar.Enabled = value;
            }
        }
        
        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;
                frame.Rect = value;
                scrollBar.Rect = scrollBar.IsHorizontal ?
                    new Rectangle(rect.X, rect.Bottom - 20, rect.Width, 20) :
                    new Rectangle(rect.Right - 20, rect.Y, 20, rect.Height);            
            }
        }

        public override Color Color
        {
            get
            {
                return base.Color;
            }
            set
            {
                base.Color = value;

                frame.Color = value;
            }
        }

        public bool ScrollBarEnabled
        {
            get { return scrollBarEnabled; }
            set
            {
                scrollBarEnabled = value;
            }
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, string style, GUIComponent parent = null)
            : this(rect, style, Alignment.TopLeft, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, string style, Alignment alignment, GUIComponent parent = null)
            : this(rect, null, alignment, style, parent, false)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, Color? color, string style = null, GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, Color? color, Alignment alignment, string style = null, GUIComponent parent = null, bool isHorizontal = false)
            : base(style)
        {
            this.rect = rect;
            this.alignment = alignment;

            selected = new List<GUIComponent>();

            if (color != null) this.color = (Color)color;

            if (parent != null)
                parent.AddChild(this);

            scrollBarHidden = true;

            if (isHorizontal)
            {
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.X, this.rect.Bottom - 20, this.rect.Width, 20), null, 1.0f, "");
            }
            else
            {
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.Right - 20, this.rect.Y, 20, this.rect.Height), null, 1.0f, "");
            }

            scrollBar.IsHorizontal = isHorizontal;

            frame = new GUIFrame(new Rectangle(0, 0, this.rect.Width, this.rect.Height), style, this);
            if (style != null) GUI.Style.Apply(frame, "", this);

            UpdateScrollBarSize();

            Children.Clear();

            enabled = true;

            scrollBarEnabled = true;

            scrollBar.BarScroll = 0.0f;
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIListBox(RectTransform rectT, bool isHorizontal = false, Color? color = null, string style = null) : base(style, rectT)
        {
            selected = new List<GUIComponent>();
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT), style);
            if (style != null) GUI.Style.Apply(frame, style, this);
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            scrollBarHidden = true;
            if (isHorizontal)
            {
                scrollBar = new GUIScrollBar(new RectTransform(new Point(Rect.Width, 20), rectT, Anchor.BottomLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, 20) });
            }
            else
            {
                scrollBar = new GUIScrollBar(new RectTransform(new Point(20, Rect.Height), rectT, Anchor.TopRight, Pivot.TopLeft) { AbsoluteOffset = new Point(20, 0) });
            }
            scrollBar.IsHorizontal = isHorizontal;
            UpdateScrollBarSize();
            enabled = true;
            scrollBarEnabled = true;
            scrollBar.BarScroll = 0.0f;
            padding = Vector4.Zero;
        }

        private bool IgnoreChild(GUIComponent child)
        {
            return child == scrollBar || child == frame || !child.Visible;
        }

        public void Select(object userData, bool force = false)
        {
            var children = Children;
            for (int i = 0; i < children.Count; i++)
            {
                if ((children[i].UserData != null && children[i].UserData.Equals(userData)) ||
                    (children[i].UserData == null && userData == null))
                {
                    Select(i, force);
                    if (!SelectMultiple) return;
                }
            }
        }

        public override void SetDimensions(Point size, bool expandChildren = false)
        {
            base.SetDimensions(size, expandChildren);

            // TODO: does not work with RectTransform
            if (scrollBar.IsHorizontal)
            {
                scrollBar.Rect = new Rectangle(Rect.X, Rect.Bottom - 20, Rect.Width, 20);
            }
            else
            {
                scrollBar.Rect = new Rectangle(Rect.Right - 20, Rect.Y, 20, Rect.Height);
            }

            UpdateScrollBarSize();
        }

        private void UpdateChildrenRect()
        {
            var children = Children;
            int x = Rect.X, y = Rect.Y;
            if (RectTransform != null)
            {
                x = 0;
                y = 0;
            }
            if (!scrollBarHidden)
            {
                if (scrollBar.IsHorizontal)
                {
                    x -= (int)((totalSize - Rect.Width) * scrollBar.BarScroll);
                }
                else
                {
                    y -= (int)((totalSize - Rect.Height) * scrollBar.BarScroll);
                }
            }

            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (IgnoreChild(child)) { continue; }
                if (RectTransform != null)
                {
                    child.RectTransform.AbsoluteOffset = new Point(x, y);
                }
                else
                {
                    child.Rect = new Rectangle(x, y, child.Rect.Width, child.Rect.Height);
                }
                if (scrollBar.IsHorizontal)
                {
                    x += child.Rect.Width + spacing;
                }
                else
                {
                    y += child.Rect.Height + spacing;
                }

                // selecting
                if (enabled && child.CanBeFocused && (GUI.IsMouseOn(child)) && child.Rect.Contains(PlayerInput.MousePosition))
                {
                    child.State = ComponentState.Hover;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        Select(i);
                    }
                }
                else if (selected.Contains(child))
                {
                    child.State = ComponentState.Selected;

                    if (CheckSelected != null)
                    {
                        if (CheckSelected() != child.UserData) selected.Remove(child);
                    }
                }
                else
                {
                    child.State = ComponentState.None;
                }
            }
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) { return; }
            base.AddToGUIUpdateList(true, order);
            if (ignoreChildren) { return; }
            var children = Children;
            int lastVisible = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == scrollBar) { continue; }

                if (!IsChildVisible(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }

                lastVisible = i;
                child.AddToGUIUpdateList(false, order);
            }

            if (scrollBarEnabled && !scrollBarHidden)
            {
                scrollBar.AddToGUIUpdateList(false, order);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            UpdateChildrenRect();

            if ((GUI.IsMouseOn(this) || GUI.IsMouseOn(scrollBar)) && PlayerInput.ScrollWheelSpeed != 0)
            {
                scrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * BarSize;
            }
        }

        public void Select(int childIndex, bool force = false)
        {
            var children = Children;
            if (childIndex >= children.Count || childIndex < 0) return;

            bool wasSelected = true;
            if (OnSelected != null) wasSelected = OnSelected(children[childIndex], children[childIndex].UserData) || force;
            
            if (!wasSelected) return;

            if (SelectMultiple)
            {
                if (selected.Contains(children[childIndex]))
                {
                    selected.Remove(children[childIndex]);
                }
                else
                {
                    selected.Add(children[childIndex]);
                }
            }
            else
            {
                selected.Clear();
                selected.Add(children[childIndex]);
            }

        }

        public void Deselect()
        {
            selected.Clear();
        }

        public void UpdateScrollBarSize()
        {
            var children = Children;
            totalSize = (int)(padding.Y + padding.W);
            if (frame != null)
            {
                totalSize += (int)(frame.Padding.Y + frame.Padding.W);
            }

            foreach (GUIComponent child in children)
            {
                if (IgnoreChild(child)) { continue; }
                totalSize += (scrollBar.IsHorizontal) ? child.Rect.Width : child.Rect.Height;
            }

            totalSize += (children.Count - 1) * spacing;

            scrollBar.BarSize = scrollBar.IsHorizontal ?
                Math.Max(Math.Min((float)Rect.Width / (float)totalSize, 1.0f), 5.0f / Rect.Width) :
                Math.Max(Math.Min((float)Rect.Height / (float)totalSize, 1.0f), 5.0f / Rect.Height);

            scrollBarHidden = scrollBar.BarSize >= 1.0f;
        }

        public override void AddChild(GUIComponent child)
        {
            if (child.RectTransform != null)
            {
                child.RectTransform.Parent = RectTransform;
            }
            else
            {
                base.AddChild(child);
            }
            UpdateScrollBarSize();
            // Handle resizing, if the scroll bar size visibility has changed
            if (frame != null)
            {
                if (!scrollBarHidden && !hasBeenResized)
                {
                    int x = scrollBar.IsHorizontal ? 0 : scrollBar.Rect.Width;
                    int y = scrollBar.IsHorizontal ? scrollBar.Rect.Height : 0;
                    if (frame.RectTransform != null)
                    {
                        frame.RectTransform.Resize(new Point(Rect.Width - x, Rect.Height - y), resizeChildren: true);
                    }
                    else
                    {
                        frame.Rect = new Rectangle(frame.Rect.X, frame.Rect.Y, Rect.Width - x, Rect.Height - y);
                    }
                    hasBeenResized = true;
                }
                else if (scrollBarHidden && hasBeenResized)
                {
                    if (frame.RectTransform != null)
                    {
                        frame.RectTransform.Resize(new Point(Rect.Width, Rect.Height), resizeChildren: true);
                    }
                    else
                    {
                        frame.Rect = Rect;
                    }
                    hasBeenResized = false;
                }
            }

            UpdateChildrenRect();
        }

        public override void ClearChildren()
        {
            base.ClearChildren();
            selected.Clear();
        }

        public override void RemoveChild(GUIComponent child)
        {
            if (RectTransform != null)
            {
                RectTransform.RemoveChild(child.RectTransform);
            }
            else
            {
                if (child == null) return;
                base.RemoveChild(child);
                if (selected.Contains(child)) selected.Remove(child);
            }
            UpdateScrollBarSize();
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            
            frame.DrawManually(spriteBatch, alsoChildren: true, recursive: true);

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, frame.Rect);

            var children = Children;
            int lastVisible = 0;
            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (IgnoreChild(child)) { continue; }

                if (!IsChildVisible(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }
                lastVisible = i;
                child.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
            }
            
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;

            if (!scrollBarHidden) scrollBar.DrawManually(spriteBatch, alsoChildren: true, recursive: true);

            //// Debug
            //GUI.DrawString(spriteBatch, new Vector2(800, 0), "scroll bar total size: " + totalSize.ToString(), Color.White, Color.Black * 0.5f);
            //GUI.DrawString(spriteBatch, new Vector2(800, 40), "child count: " + Children.Where(c => !IgnoreChild(c)).Count().ToString(), Color.White, Color.Black * 0.5f);
            //int y = 40;
            //foreach (var child in Children)
            //{
            //    if (IgnoreChild(child)) { continue; }
            //    if (child.RectTransform == null) { continue; }
            //    y += 40;
            //    GUI.DrawString(spriteBatch, new Vector2(800, y), $"Location: {child.Rect.Location}, Size: {child.Rect.Size}, Offset: {child.RectTransform.AbsoluteOffset}", Color.White, Color.Black * 0.5f);
            //}
        }

        private bool IsChildVisible(GUIComponent child)
        {
            if (child == null) return false;

            if (scrollBar.IsHorizontal)
            {
                if (child.Rect.Right < Rect.X) return false;
                if (child.Rect.X > Rect.Right) return false;
            }
            else
            {
                if (child.Rect.Bottom < Rect.Y) return false;
                if (child.Rect.Y > Rect.Bottom) return false;
            }

            return true;
        }
    }
}
