﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class RangedWeapon : ItemComponent
    {
        private float reload, reloadTimer;

        private Vector2 barrelPos;

        [Serialize("0.0,0.0", false, description: "The position of the barrel as an offset from the item's center (in pixels). Determines where the projectiles spawn.")]
        public string BarrelPos
        {
            get { return XMLExtensions.Vector2ToString(ConvertUnits.ToDisplayUnits(barrelPos)); }
            set { barrelPos = ConvertUnits.ToSimUnits(XMLExtensions.ParseVector2(value)); }
        }

        [Serialize(1.0f, false, description: "How long the user has to wait before they can fire the weapon again (in seconds).")]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(value, 0.0f); }
        }

        [Serialize(1, false, description: "How projectiles the weapon launches when fired once.")]
        public int ProjectileCount
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles when used by a character with sufficient skills to use the weapon (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles when used by a character with insufficient skills to use the weapon (in degrees).")]
        public float UnskilledSpread
        {
            get;
            set;
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                Vector2 flippedPos = barrelPos;
                if (item.body.Dir < 0.0f) flippedPos.X = -flippedPos.X;
                return Vector2.Transform(flippedPos, bodyTransform);
            }
        }
                
        public RangedWeapon(Item item, XElement element)
            : base(item, element)
        {
            item.IsShootable = true;
            // TODO: should define this in xml if we have ranged weapons that don't require aim to use
            item.RequireAimToUse = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            reloadTimer -= deltaTime;

            if (reloadTimer < 0.0f)
            {
                reloadTimer = 0.0f;
                IsActive = false;
            }
        }

        private float GetSpread(Character user)
        {
            float degreeOfFailure = 1.0f - DegreeOfSuccess(user);
            degreeOfFailure *= degreeOfFailure;
            return MathHelper.ToRadians(MathHelper.Lerp(Spread, UnskilledSpread, degreeOfFailure));
        }

        private readonly List<Body> limbBodies = new List<Body>();
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character.Removed) { return false; }
            if ((item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) || reloadTimer > 0.0f) { return false; }

            IsActive = true;
            reloadTimer = reload;

            if (item.AiTarget != null)
            {
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
            }

            limbBodies.Clear();
            foreach (Limb l in character.AnimController.Limbs)
            {
                if (l.IsSevered) { continue; }
                limbBodies.Add(l.body.FarseerBody);
            }

            float degreeOfFailure = 1.0f - DegreeOfSuccess(character);
            degreeOfFailure *= degreeOfFailure;
            if (degreeOfFailure > Rand.Range(0.0f, 1.0f))
            {
                ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);
            }

            for (int i = 0; i < ProjectileCount; i++)
            {
                Projectile projectile = FindProjectile(triggerOnUseOnContainers: true);
                if (projectile == null) { return true; }

                float spread = GetSpread(character);
                float rotation = (item.body.Dir == 1.0f) ? item.body.Rotation : item.body.Rotation - MathHelper.Pi;
                rotation += spread * Rand.Range(-0.5f, 0.5f);

                projectile.User = character;
                //add the limbs of the shooter to the list of bodies to be ignored
                //so that the player can't shoot himself
                projectile.IgnoredBodies = new List<Body>(limbBodies);

                Vector2 projectilePos = item.SimPosition;
                Vector2 sourcePos = character?.AnimController == null ? item.SimPosition : character.AnimController.AimSourceSimPos;
                Vector2 barrelPos = TransformedBarrelPos + item.body.SimPosition;
                //make sure there's no obstacles between the base of the weapon (or the shoulder of the character) and the end of the barrel
                if (Submarine.PickBody(sourcePos, barrelPos, projectile.IgnoredBodies, Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking) == null)
                {
                    //no obstacles -> we can spawn the projectile at the barrel
                    projectilePos = barrelPos;
                }
                else if ((sourcePos - barrelPos).LengthSquared() > 0.0001f)
                {
                    //spawn the projectile body.GetMaxExtent() away from the position where the raycast hit the obstacle
                    projectilePos = sourcePos - Vector2.Normalize(barrelPos - projectilePos) * Math.Max(projectile.Item.body.GetMaxExtent(), 0.1f);
                }

                projectile.Item.body.ResetDynamics();
                projectile.Item.SetTransform(projectilePos, rotation);

                projectile.Use(deltaTime);
                projectile.Item.GetComponent<Rope>()?.Attach(item, projectile.Item);
                if (projectile.Item.Removed) { continue; }
                projectile.User = character;

                projectile.Item.body.ApplyTorque(projectile.Item.body.Mass * degreeOfFailure * Rand.Range(-10.0f, 10.0f));

                //set the rotation of the projectile again because dropping the projectile resets the rotation
                projectile.Item.SetTransform(projectilePos,
                    rotation + (projectile.Item.body.Dir * projectile.LaunchRotationRadians));

                item.RemoveContained(projectile.Item);

                if (i == 0)
                {
                    //recoil
                    item.body.ApplyLinearImpulse(
                        new Vector2((float)Math.Cos(projectile.Item.body.Rotation), (float)Math.Sin(projectile.Item.body.Rotation)) * item.body.Mass * -50.0f,
                        maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                }
            }

            LaunchProjSpecific();

            return true;
        }

        private Projectile FindProjectile(bool triggerOnUseOnContainers = false)
        {
            var containedItems = item.ContainedItems;
            if (containedItems == null) { return null; }

            foreach (Item item in containedItems)
            {
                Projectile projectile = item.GetComponent<Projectile>();
                if (projectile != null) { return projectile; }
            }

            //projectile not found, see if one of the contained items contains projectiles
            foreach (Item item in containedItems)
            {
                var containedSubItems = item.ContainedItems;
                if (containedSubItems == null) { continue; }
                foreach (Item subItem in containedSubItems)
                {
                    Projectile projectile = subItem.GetComponent<Projectile>();
                    //apply OnUse statuseffects to the container in case it has to react to it somehow
                    //(play a sound, spawn more projectiles, reduce condition...)
                    if (triggerOnUseOnContainers && subItem.Condition > 0.0f)
                    {
                        subItem.GetComponent<ItemContainer>()?.Item.ApplyStatusEffects(ActionType.OnUse, 1.0f);
                    }
                    if (projectile != null) { return projectile; }
                }
            }
            
            return null;
        }

        partial void LaunchProjSpecific();
    }
}
