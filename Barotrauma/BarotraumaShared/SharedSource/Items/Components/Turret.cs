﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using FarseerPhysics.Dynamics;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite barrelSprite, railSprite;

        private Vector2 barrelPos;
        private Vector2 transformedBarrelPos;

        private LightComponent lightComponent;
        
        private float rotation, targetRotation;

        private float reload, reloadTime;

        private float minRotation, maxRotation;

        private float launchImpulse;

        private Camera cam;

        private float angularVelocity;

        private int failedLaunchAttempts;

        private readonly List<Item> activeProjectiles = new List<Item>();
        public IEnumerable<Item> ActiveProjectiles => activeProjectiles;

        private Character user;

        private float resetUserTimer;

        public float Rotation
        {
            get { return rotation; }
        }
        
        [Serialize("0,0", false, description: "The position of the barrel relative to the upper left corner of the base sprite (in pixels).")]
        public Vector2 BarrelPos
        {
            get 
            { 
                return barrelPos; 
            }
            set
            { 
                barrelPos = value;
                UpdateTransformedBarrelPos();
            }
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                return transformedBarrelPos;
            }
        }

        [Serialize(0.0f, false, description: "The impulse applied to the physics body of the projectile (the higher the impulse, the faster the projectiles are launched).")]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Editable(0.0f, 1000.0f), Serialize(5.0f, false, description: "The period of time the user has to wait between shots.")]
        public float Reload
        {
            get { return reloadTime; }
            set { reloadTime = value; }
        }

        [Editable(0.1f, 10f), Serialize(1.0f, false, description: "Modifies the duration of retraction of the barrell after recoil to get back to the original position after shooting. Reload time affects this too.")]
        public float RetractionDurationMultiplier
        {
            get;
            set;
        }

        [Editable(0.1f, 10f), Serialize(0.1f, false, description: "How quickly the recoil moves the barrel after launching.")]
        public float RecoilTime
        {
            get;
            set;
        }

        [Editable(0f, 1000f), Serialize(0f, false, description: "How long the barrell stays in place after the recoil and before retracting back to the original position.")]
        public float RetractionDelay
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many projectiles the weapon launches when fired once.")]
        public int ProjectileCount
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the turret be fired without projectiles (causing it just to execute the OnUse effects and the firing animation without actually firing anything).")]
        public bool LaunchWithoutProjectile
        {
            get;
            set;
        }

        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }), 
            Serialize("0.0,0.0", true, description: "The range at which the barrel can rotate.", alwaysUseInstanceValues: true)]
        public Vector2 RotationLimits
        {
            get
            {
                return new Vector2(MathHelper.ToDegrees(minRotation), MathHelper.ToDegrees(maxRotation)); 
            }
            set
            {
                minRotation = MathHelper.ToRadians(Math.Min(value.X, value.Y));
                maxRotation = MathHelper.ToRadians(Math.Max(value.X, value.Y));

                rotation = (minRotation + maxRotation) / 2;
#if CLIENT
                if (lightComponent != null) 
                {
                    lightComponent.Rotation = rotation;
                    lightComponent.Light.Rotation = -rotation;
                }
#endif
            }
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(5.0f, false, description: "How much torque is applied to rotate the barrel when the item is used by a character"
            + " with insufficient skills to operate it. Higher values make the barrel rotate faster.")]
        public float SpringStiffnessLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(2.0f, false, description: "How much torque is applied to rotate the barrel when the item is used by a character"
            + " with sufficient skills to operate it. Higher values make the barrel rotate faster.")]
        public float SpringStiffnessHighSkill
        {
            get;
            private set;
        }

        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(50.0f, false, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character"
            + " with insufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at.")]
        public float SpringDampingLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 1000.0f, DecimalCount = 2),
            Serialize(10.0f, false, description: "How much torque is applied to resist the movement of the barrel when the item is used by a character"
            + " with sufficient skills to operate it. Higher values make the aiming more \"snappy\", stopping the barrel from swinging around the direction it's being aimed at.")]
        public float SpringDampingHighSkill
        {
            get;
            private set;
        }

        [Editable(0.0f, 100.0f, DecimalCount = 2),
            Serialize(1.0f, false, description: "Maximum angular velocity of the barrel when used by a character with insufficient skills to operate it.")]
        public float RotationSpeedLowSkill
        {
            get;
            private set;
        }
        [Editable(0.0f, 100.0f, DecimalCount = 2),
            Serialize(5.0f, false, description: "Maximum angular velocity of the barrel when used by a character with sufficient skills to operate it."),]
        public float RotationSpeedHighSkill
        {
            get;
            private set;
        }

        private float baseRotationRad;
        [Editable(0.0f, 360.0f), Serialize(0.0f, true, description: "The angle of the turret's base in degrees.", alwaysUseInstanceValues: true)]
        public float BaseRotation
        {
            get { return MathHelper.ToDegrees(baseRotationRad); }
            set
            {
                baseRotationRad = MathHelper.ToRadians(value);
                UpdateTransformedBarrelPos();
            }
        }

        [Serialize(3000.0f, true, description: "How close to a target the turret has to be for an AI character to fire it.")]
        public float AIRange
        {
            get;
            set;
        }

        [Serialize(-1, true, description: "The turret won't fire additional projectiles if the number of previously fired, still active projectiles reaches this limit. If set to -1, there is no limit to the number of projectiles.")]
        public int MaxActiveProjectiles
        {
            get;
            set;
        }

        public Turret(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "barrelsprite":
                        barrelSprite = new Sprite(subElement);
                        break;
                    case "railsprite":
                        railSprite = new Sprite(subElement);
                        break;
                }
            }
            item.IsShootable = true;
            item.RequireAimToUse = false;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        private void UpdateTransformedBarrelPos()
        {
            float flippedRotation = BaseRotation;
            if (item.FlippedX) flippedRotation = -flippedRotation;
            //if (item.FlippedY) flippedRotation = 180.0f - flippedRotation;
            transformedBarrelPos = MathUtils.RotatePointAroundTarget(barrelPos * item.Scale, new Vector2(item.Rect.Width / 2, item.Rect.Height / 2), flippedRotation);
#if CLIENT
            item.ResetCachedVisibleSize();
            item.SpriteRotation = MathHelper.ToRadians(flippedRotation);
#endif
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var lightComponents = item.GetComponents<LightComponent>();
            if (lightComponents != null && lightComponents.Count() > 0)
            {
                lightComponent = lightComponents.FirstOrDefault(lc => lc.Parent == this);
#if CLIENT
                if (lightComponent != null) 
                {
                    lightComponent.Parent = null;
                    lightComponent.Rotation = rotation;
                    lightComponent.Light.Rotation = -rotation;
                }
#endif
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            this.cam = cam;

            if (reload > 0.0f) { reload -= deltaTime; }

            if (user != null && user.Removed)
            {
                user = null;
            }
            else
            {
                resetUserTimer -= deltaTime;
                if (resetUserTimer <= 0.0f) { user = null; }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            UpdateProjSpecific(deltaTime);

            if (minRotation == maxRotation) { return; }

            float targetMidDiff = MathHelper.WrapAngle(targetRotation - (minRotation + maxRotation) / 2.0f);

            float maxDist = (maxRotation - minRotation) / 2.0f;

            if (Math.Abs(targetMidDiff) > maxDist)
            {
                targetRotation = (targetMidDiff < 0.0f) ? minRotation : maxRotation;
            }

            float degreeOfSuccess = user == null ? 0.5f : DegreeOfSuccess(user);
            if (degreeOfSuccess < 0.5f) { degreeOfSuccess *= degreeOfSuccess; } //the ease of aiming drops quickly with insufficient skill levels
            float springStiffness = MathHelper.Lerp(SpringStiffnessLowSkill, SpringStiffnessHighSkill, degreeOfSuccess);
            float springDamping = MathHelper.Lerp(SpringDampingLowSkill, SpringDampingHighSkill, degreeOfSuccess);
            float rotationSpeed = MathHelper.Lerp(RotationSpeedLowSkill, RotationSpeedHighSkill, degreeOfSuccess);

            if (user?.Info != null)
            {
                user.Info.IncreaseSkillLevel("weapons",
                    SkillSettings.Current.SkillIncreasePerSecondWhenOperatingTurret * deltaTime / Math.Max(user.GetSkillLevel("weapons"), 1.0f),
                    user.WorldPosition + Vector2.UnitY * 150.0f);
            }

            angularVelocity += 
                (MathHelper.WrapAngle(targetRotation - rotation) * springStiffness - angularVelocity * springDamping) * deltaTime;
            angularVelocity = MathHelper.Clamp(angularVelocity, -rotationSpeed, rotationSpeed);

            rotation += angularVelocity * deltaTime;

            float rotMidDiff = MathHelper.WrapAngle(rotation - (minRotation + maxRotation) / 2.0f);

            if (rotMidDiff < -maxDist)
            {
                rotation = minRotation;
                angularVelocity *= -0.5f;
            } 
            else if (rotMidDiff > maxDist)
            {
                rotation = maxRotation;
                angularVelocity *= -0.5f;
            }

            if (lightComponent != null)
            {
                lightComponent.Rotation = rotation;
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        public override bool Use(float deltaTime, Character character = null)
        {
            if (!characterUsable && character != null) { return false; }
            return TryLaunch(deltaTime, character);
        }

        private bool TryLaunch(float deltaTime, Character character = null, bool ignorePower = false)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }

            if (reload > 0.0f) { return false; }

            if (MaxActiveProjectiles >= 0)
            {
                activeProjectiles.RemoveAll(it => it.Removed);
                if (activeProjectiles.Count >= MaxActiveProjectiles)
                {
                    return false;
                }
            }
            
            if (!ignorePower)
            {
                if (GetAvailableBatteryPower() < powerConsumption)
                {
#if CLIENT
                    if (!flashLowPower && character != null && character == Character.Controlled)
                    {
                        flashLowPower = true;
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
#endif
                    return false;
                }
            }

            Projectile launchedProjectile = null;
            for (int i = 0; i < ProjectileCount; i++)
            {
                foreach (MapEntity e in item.linkedTo)
                {
                    //use linked projectile containers in case they have to react to the turret being launched somehow
                    //(play a sound, spawn more projectiles)
                    if (!(e is Item linkedItem)) { continue; }
                    ItemContainer projectileContainer = linkedItem.GetComponent<ItemContainer>();
                    if (projectileContainer != null)
                    {
                        linkedItem.Use(deltaTime, null);
                        var repairable = linkedItem.GetComponent<Repairable>();
                        if (repairable != null && failedLaunchAttempts < 2)
                        {
                            repairable.LastActiveTime = (float)Timing.TotalTime + 1.0f;
                        }
                    }
                }
                var projectiles = GetLoadedProjectiles(true);
                if (projectiles.Count == 0 && !LaunchWithoutProjectile)
                {
                    //coilguns spawns ammo in the ammo boxes with the OnUse statuseffect when the turret is launched,
                    //causing a one frame delay before the gun can be launched (or more in multiplayer where there may be a longer delay)
                    //  -> attempt to launch the gun multiple times before showing the "no ammo" flash
                    failedLaunchAttempts++;
#if CLIENT
                    if (!flashNoAmmo && character != null && character == Character.Controlled && failedLaunchAttempts > 20)
                    {
                        flashNoAmmo = true;
                        failedLaunchAttempts = 0;
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
#endif
                    return false;
                }
                failedLaunchAttempts = 0;
                launchedProjectile = projectiles.FirstOrDefault();

                if (!ignorePower)
                {
                    var batteries = item.GetConnectedComponents<PowerContainer>();
                    float neededPower = powerConsumption;
                    while (neededPower > 0.0001f && batteries.Count > 0)
                    {
                        batteries.RemoveAll(b => b.Charge <= 0.0001f || b.MaxOutPut <= 0.0001f);
                        float takePower = neededPower / batteries.Count;
                        takePower = Math.Min(takePower, batteries.Min(b => Math.Min(b.Charge * 3600.0f, b.MaxOutPut)));
                        foreach (PowerContainer battery in batteries)
                        {
                            neededPower -= takePower;
                            battery.Charge -= takePower / 3600.0f;
#if SERVER
                            battery.Item.CreateServerEvent(battery);                        
#endif
                        }
                    }
                }

                if (launchedProjectile != null || LaunchWithoutProjectile)
                {
                    Launch(launchedProjectile?.Item, character);
                }
            }

#if SERVER
            if (character != null && launchedProjectile != null)
            {
                string msg = GameServer.CharacterLogName(character) + " launched " + item.Name + " (projectile: " + launchedProjectile.Item.Name;
                var containedItems = launchedProjectile.Item.ContainedItems;
                if (containedItems == null || !containedItems.Any())
                {
                    msg += ")";
                }
                else
                {
                    msg += ", contained items: " + string.Join(", ", containedItems.Select(i => i.Name)) + ")";
                }
                GameServer.Log(msg, ServerLog.MessageType.ItemInteraction);
            }
#endif

            return true;
        }

        private void Launch(Item projectile, Character user = null, float? launchRotation = null)
        {
            reload = reloadTime;

            if (projectile != null)
            {
                activeProjectiles.Add(projectile);
                projectile.Drop(null);
                if (projectile.body != null) 
                {                 
                    projectile.body.Dir = 1.0f;
                    projectile.body.ResetDynamics();
                    projectile.body.Enabled = true;
                }

                float spread = MathHelper.ToRadians(Spread) * Rand.Range(-0.5f, 0.5f);
                projectile.SetTransform(
                    ConvertUnits.ToSimUnits(new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y)), 
                    -(launchRotation ?? rotation) + spread);
                projectile.UpdateTransform();
                projectile.Submarine = projectile.body?.Submarine;

                Projectile projectileComponent = projectile.GetComponent<Projectile>();
                if (projectileComponent != null)
                {
                    projectileComponent.Use((float)Timing.Step);
                    projectile.GetComponent<Rope>()?.Attach(item, projectile);
                    projectileComponent.User = user;
                }

                if (projectile.Container != null) { projectile.Container.RemoveContained(projectile); }            
            }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), projectile });
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, user: user);
            LaunchProjSpecific();
        }

        partial void LaunchProjSpecific();

        private float waitTimer;
        private float disorderTimer;

        private float prevTargetRotation;
        private float updateTimer;
        private bool updatePending;
        public void ThalamusOperate(WreckAI ai, float deltaTime, bool targetHumans, bool targetOtherCreatures, bool targetSubmarines, bool ignoreDelay)
        {
            if (ai == null) { return; }

            IsActive = true;

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return;
            }

            if (updatePending)
            {
                if (updateTimer < 0.0f)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    prevTargetRotation = targetRotation;
                    updateTimer = 0.25f;
                }
                updateTimer -= deltaTime;
            }

            if (!ignoreDelay && waitTimer > 0)
            {
                waitTimer -= deltaTime;
                return;
            }
            Submarine closestSub = null;
            float maxDistance = 10000.0f;
            float shootDistance = AIRange;
            ISpatialEntity target = null;
            float closestDist = shootDistance * shootDistance;
            if (targetHumans || targetOtherCreatures)
            {
                foreach (var character in Character.CharacterList)
                {
                    if (character == null || character.Removed || character.IsDead) { continue; }
                    if (character.Params.Group.Equals(ai.Config.Entity, StringComparison.OrdinalIgnoreCase)) { continue; }
                    bool isHuman = character.IsHuman || character.Params.Group.Equals(CharacterPrefab.HumanSpeciesName, StringComparison.OrdinalIgnoreCase);
                    if (isHuman)
                    {
                        if (!targetHumans)
                        {
                            // Don't target humans if not defined to.
                            continue;
                        }
                    }
                    else if (!targetOtherCreatures)
                    {
                        // Don't target other creatures if not defined to.
                        continue;
                    }
                    float dist = Vector2.DistanceSquared(character.WorldPosition, item.WorldPosition);
                    if (dist > closestDist) { continue; }
                    target = character;
                    closestDist = dist;
                }
            }
            if (targetSubmarines)
            {
                if (target == null || target.Submarine != null)
                {
                    closestDist = maxDistance * maxDistance;
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.Info.Type != SubmarineInfo.SubmarineType.Player) { continue; }
                        float dist = Vector2.DistanceSquared(sub.WorldPosition, item.WorldPosition);
                        if (dist > closestDist) { continue; }
                        closestSub = sub;
                        closestDist = dist;
                    }
                    closestDist = shootDistance * shootDistance;
                    if (closestSub != null)
                    {
                        foreach (var hull in Hull.hullList)
                        {
                            if (!closestSub.IsEntityFoundOnThisSub(hull, true)) { continue; }
                            float dist = Vector2.DistanceSquared(hull.WorldPosition, item.WorldPosition);
                            if (dist > closestDist) { continue; }
                            target = hull;
                            closestDist = dist;
                        }
                    }
                }
            }
            if (!ignoreDelay)
            {
                if (target == null)
                {
                    // Random movement
                    waitTimer = Rand.Value(Rand.RandSync.Unsynced) < 0.98f ? 0f : Rand.Range(5f, 20f);
                    targetRotation = Rand.Range(minRotation, maxRotation);
                    updatePending = true;
                    return;
                }
                if (disorderTimer < 0)
                {
                    // Random disorder
                    disorderTimer = Rand.Range(0f, 3f);
                    waitTimer = Rand.Range(0.25f, 1f);
                    targetRotation = MathUtils.WrapAngleTwoPi(targetRotation += Rand.Range(-1f, 1f));
                    updatePending = true;
                    return;
                }
                else
                {
                    disorderTimer -= deltaTime;
                }
            }
            if (target == null) { return; }
      
            float angle = -MathUtils.VectorToAngle(target.WorldPosition - item.WorldPosition);
            targetRotation = MathUtils.WrapAngleTwoPi(angle);

            if (Math.Abs(targetRotation - prevTargetRotation) > 0.1f) { updatePending = true; }

            if (target is Hull targetHull)
            {
                Vector2 barrelDir = new Vector2((float)Math.Cos(rotation), -(float)Math.Sin(rotation));
                if (!MathUtils.GetLineRectangleIntersection(item.WorldPosition, item.WorldPosition + barrelDir * AIRange, targetHull.WorldRect, out _))
                {
                    return;
                }
            }
            else
            {
                float midRotation = (minRotation + maxRotation) / 2.0f;
                while (midRotation - angle < -MathHelper.Pi) { angle -= MathHelper.TwoPi; }
                while (midRotation - angle > MathHelper.Pi) { angle += MathHelper.TwoPi; }
                if (angle < minRotation || angle > maxRotation) { return; }
                float enemyAngle = MathUtils.VectorToAngle(target.WorldPosition - item.WorldPosition);
                float turretAngle = -rotation;
                if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.15f) { return; }
            }

            Vector2 start = ConvertUnits.ToSimUnits(item.WorldPosition);
            Vector2 end = ConvertUnits.ToSimUnits(target.WorldPosition);
            if (target.Submarine != null)
            {
                start -= target.Submarine.SimPosition;
                end -= target.Submarine.SimPosition;
            }
            var collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel;
            var pickedBody = Submarine.PickBody(start, end, null, collisionCategories, allowInsideFixture: true,
               customPredicate: (Fixture f) => { return !item.StaticFixtures.Contains(f); });
            if (pickedBody == null) { return; }
            Character targetCharacter = null;
            if (pickedBody.UserData is Character c)
            {
                targetCharacter = c;
            }
            else if (pickedBody.UserData is Limb limb)
            {
                targetCharacter = limb.character;
            }
            if (targetCharacter != null)
            {
                if (targetCharacter.Params.Group.Equals(ai.Config.Entity, StringComparison.OrdinalIgnoreCase))
                {
                    // Don't shoot friendly characters
                    return;
                }
            }
            else
            {
                if (pickedBody.UserData is ISpatialEntity e)
                {
                    Submarine sub = e.Submarine;
                    if (sub == null) { return; }
                    if (!targetSubmarines) { return; }
                    if (sub == Item.Submarine) { return; }
                    // Don't shoot non-player submarines, i.e. wrecks or outposts.
                    if (!sub.Info.IsPlayer) { return; }
                }
                else
                {
                    // Hit something else, probably a level wall
                    return;
                }
            }
            TryLaunch(deltaTime, ignorePower: true);
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (character.AIController.SelectedAiTarget?.Entity is Character previousTarget &&
                previousTarget.IsDead)
            {
                character?.Speak(TextManager.Get("DialogTurretTargetDead"), null, 0.0f, "killedtarget" + previousTarget.ID, 30.0f);
                character.AIController.SelectTarget(null);
            }

            if (GetAvailableBatteryPower() < powerConsumption)
            {
                var batteries = item.GetConnectedComponents<PowerContainer>();

                float lowestCharge = 0.0f;
                PowerContainer batteryToLoad = null;
                foreach (PowerContainer battery in batteries)
                {
                    if (battery.Item.NonInteractable) { continue; }
                    if (batteryToLoad == null || battery.Charge < lowestCharge)
                    {
                        batteryToLoad = battery;
                        lowestCharge = battery.Charge;
                    }
                }

                if (batteryToLoad == null) return true;

                if (batteryToLoad.RechargeSpeed < batteryToLoad.MaxRechargeSpeed * 0.4f)
                {
                    objective.AddSubObjective(new AIObjectiveOperateItem(batteryToLoad, character, objective.objectiveManager, option: "", requireEquip: false));                    
                    return false;
                }
            }

            int usableProjectileCount = 0;
            int maxProjectileCount = 0;
            foreach (MapEntity e in item.linkedTo)
            {
                if (item.NonInteractable) { continue; }
                if (e is Item projectileContainer)
                {
                    var containedItems = projectileContainer.ContainedItems;
                    if (containedItems != null)
                    {
                        var container = projectileContainer.GetComponent<ItemContainer>();
                        maxProjectileCount += container.Capacity;

                        int projectiles = containedItems.Count(it => it.Condition > 0.0f);
                        usableProjectileCount += projectiles;
                    }
                }
            }

            if (usableProjectileCount == 0)
            {
                ItemContainer container = null;
                Item containerItem = null;
                foreach (MapEntity e in item.linkedTo)
                {
                    containerItem = e as Item;
                    if (containerItem == null) { continue; }
                    if (containerItem.NonInteractable) { continue; }
                    if (character.AIController is HumanAIController aiController && aiController.IgnoredItems.Contains(containerItem)) { continue; }
                    container = containerItem.GetComponent<ItemContainer>();
                    if (container != null) { break; }
                }
                if (container == null || container.ContainableItems.Count == 0)
                {
                    character.Speak(TextManager.GetWithVariable("DialogCannotLoadTurret", "[itemname]", item.Name, true), null, 0.0f, "cannotloadturret", 30.0f);
                    return true;
                }
                if (objective.SubObjectives.None())
                {
                    if (!AIDecontainEmptyItems(character, objective, equip: true, sourceContainer: container))
                    {
                        return false;
                    }
                }
                if (objective.SubObjectives.None())
                {
                    var loadItemsObjective = AIContainItems<Turret>(container, character, objective, usableProjectileCount + 1, equip: true, removeEmpty: true);
                    if (loadItemsObjective == null)
                    {
                        if (usableProjectileCount == 0)
                        {
                            character.Speak(TextManager.GetWithVariable("DialogCannotLoadTurret", "[itemname]", item.Name, true), null, 0.0f, "cannotloadturret", 30.0f);
                            return true;
                        }
                    }
                    else
                    {
                        loadItemsObjective.ignoredContainerIdentifiers = new string[] { containerItem.prefab.Identifier };
                        character.Speak(TextManager.GetWithVariable("DialogLoadTurret", "[itemname]", item.Name, true), null, 0.0f, "loadturret", 30.0f);
                        return false;
                    }
                }
                if (objective.SubObjectives.Any())
                {
                    return false;
                }
            }

            //enough shells and power
            Character closestEnemy = null;
            float closestDist = AIRange * AIRange;
            foreach (Character enemy in Character.CharacterList)
            {
                // Ignore dead, friendly, and those that are inside the same sub
                if (enemy.IsDead || !enemy.Enabled || enemy.Submarine == character.Submarine) { continue; }
                if (HumanAIController.IsFriendly(character, enemy)) { continue; }
                
                float dist = Vector2.DistanceSquared(enemy.WorldPosition, item.WorldPosition);
                if (dist > closestDist) { continue; }
                
                float angle = -MathUtils.VectorToAngle(enemy.WorldPosition - item.WorldPosition);
                float midRotation = (minRotation + maxRotation) / 2.0f;
                while (midRotation - angle < -MathHelper.Pi) { angle -= MathHelper.TwoPi; }
                while (midRotation - angle > MathHelper.Pi) { angle += MathHelper.TwoPi; }

                if (angle < minRotation || angle > maxRotation) { continue; }

                closestEnemy = enemy;
                closestDist = dist;                
            }

            if (closestEnemy == null) { return false; }
            
            character.AIController.SelectTarget(closestEnemy.AiTarget);

            character.CursorPosition = closestEnemy.WorldPosition;
            if (character.Submarine != null) 
            { 
                character.CursorPosition -= character.Submarine.Position; 
            }
            
            float enemyAngle = MathUtils.VectorToAngle(closestEnemy.WorldPosition - item.WorldPosition);
            float turretAngle = -rotation;

            if (Math.Abs(MathUtils.GetShortestAngle(enemyAngle, turretAngle)) > 0.15f) { return false; }


            Vector2 start = ConvertUnits.ToSimUnits(item.WorldPosition);
            Vector2 end = ConvertUnits.ToSimUnits(closestEnemy.WorldPosition);
            if (closestEnemy.Submarine != null)
            {
                start -= closestEnemy.Submarine.SimPosition;
                end -= closestEnemy.Submarine.SimPosition;
            }
            var collisionCategories = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel;
            var pickedBody = Submarine.PickBody(start, end, null, collisionCategories, allowInsideFixture: true, 
               customPredicate: (Fixture f) => { return !item.StaticFixtures.Contains(f); });
            if (pickedBody == null) { return false; }
            Character targetCharacter = null;
            if (pickedBody.UserData is Character c)
            {
                targetCharacter = c;
            }
            else if (pickedBody.UserData is Limb limb)
            {
                targetCharacter = limb.character;
            }
            if (targetCharacter != null)
            {
                if (HumanAIController.IsFriendly(character, targetCharacter))
                {
                    // Don't shoot friendly characters
                    return false;
                }
            }
            else
            {
                if (pickedBody.UserData is ISpatialEntity e)
                {
                    Submarine sub = e.Submarine;
                    if (sub == null) { return false; }
                    if (sub == Item.Submarine) { return false; }
                    // Don't shoot non-player submarines, i.e. wrecks or outposts.
                    if (!sub.Info.IsPlayer) { return false; }
                    // Don't shoot friendly submarines.
                    if (sub.TeamID == Item.Submarine.TeamID) { return false; }
                }
                else
                {
                    // Hit something else, probably a level wall
                    return false;
                }
            }
            character?.Speak(TextManager.GetWithVariable("DialogFireTurret", "[itemname]", item.Name, true), null, 0.0f, "fireturret", 5.0f);
            character.SetInput(InputType.Shoot, true, true);
            return false;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            barrelSprite?.Remove(); barrelSprite = null;
            railSprite?.Remove(); railSprite = null;

#if CLIENT
            crosshairSprite?.Remove(); crosshairSprite = null;
            crosshairPointerSprite?.Remove(); crosshairPointerSprite = null;
            moveSoundChannel?.Dispose(); moveSoundChannel = null;
#endif
        }

        private List<Projectile> GetLoadedProjectiles(bool returnFirst = false)
        {
            List<Projectile> projectiles = new List<Projectile>();
            //check the item itself first
            CheckProjectileContainer(item, projectiles, returnFirst);
            foreach (MapEntity e in item.linkedTo)
            {
                if (e is Item projectileContainer) { CheckProjectileContainer(projectileContainer, projectiles, returnFirst); }
                if (returnFirst && projectiles.Any()) { return projectiles; }
            }

            return projectiles;
        }

        private void CheckProjectileContainer(Item projectileContainer, List<Projectile> projectiles, bool returnFirst)
        {
            var containedItems = projectileContainer.ContainedItems;
            if (containedItems == null) { return; }

            foreach (Item containedItem in containedItems)
            {
                var projectileComponent = containedItem.GetComponent<Projectile>();
                if (projectileComponent != null && projectileComponent.Item.body != null)
                {
                    projectiles.Add(projectileComponent);
                    if (returnFirst) { return; }
                }
                else
                {
                    //check if the contained item is another itemcontainer with projectiles inside it
                    if (containedItem.ContainedItems == null) { continue; }
                    foreach (Item subContainedItem in containedItem.ContainedItems)
                    {
                        projectileComponent = subContainedItem.GetComponent<Projectile>();
                        if (projectileComponent != null && projectileComponent.Item.body != null)
                        {
                            projectiles.Add(projectileComponent);
                            if (returnFirst) { return; }
                        }
                    }
                }
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            minRotation = MathHelper.Pi - minRotation;
            maxRotation = MathHelper.Pi - maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            barrelPos.X = item.Rect.Width / item.Scale - barrelPos.X;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();
        }

        public override void FlipY(bool relativeToSub)
        {
            baseRotationRad = MathUtils.WrapAngleTwoPi(baseRotationRad - MathHelper.Pi);
            UpdateTransformedBarrelPos();

            /*minRotation = -minRotation;
            maxRotation = -maxRotation;

            var temp = minRotation;
            minRotation = maxRotation;
            maxRotation = temp;

            barrelPos.Y = item.Rect.Height / item.Scale - barrelPos.Y;

            while (minRotation < 0)
            {
                minRotation += MathHelper.TwoPi;
                maxRotation += MathHelper.TwoPi;
            }
            rotation = (minRotation + maxRotation) / 2;

            UpdateTransformedBarrelPos();*/
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "position_in":
                    if (float.TryParse(signal, NumberStyles.Float, CultureInfo.InvariantCulture, out float newRotation))
                    {
                        targetRotation = MathHelper.ToRadians(newRotation);
                        IsActive = true;
                    }
                    user = sender;
                    resetUserTimer = 10.0f;
                    break;
                case "trigger_in":
                    item.Use((float)Timing.Step, sender);
                    user = sender;
                    resetUserTimer = 10.0f;
                    //triggering the Use method through item.Use will fail if the item is not characterusable and the signal was sent by a character
                    //so lets do it manually
                    if (!characterUsable && sender != null)
                    {
                        TryLaunch((float)Timing.Step, sender);
                    }
                    break;
                case "toggle_light":
                    if (lightComponent != null)
                    {
                        lightComponent.IsOn = !lightComponent.IsOn;
                    }
                    break;
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            if (extraData.Length > 2)
            {
                msg.Write(!(extraData[2] is Item item) || item.Removed ? ushort.MaxValue : item.ID);
                msg.WriteRangedSingle(MathHelper.Clamp(rotation, minRotation, maxRotation), minRotation, maxRotation, 16);
            }
            else
            {
                msg.Write((ushort)0);
                float wrappedTargetRotation = targetRotation;
                while (wrappedTargetRotation < minRotation && MathUtils.IsValid(wrappedTargetRotation))
                {
                    wrappedTargetRotation += MathHelper.TwoPi;
                }
                while (wrappedTargetRotation > maxRotation && MathUtils.IsValid(wrappedTargetRotation))
                {
                    wrappedTargetRotation -= MathHelper.TwoPi;
                }
                msg.WriteRangedSingle(MathHelper.Clamp(wrappedTargetRotation, minRotation, maxRotation), minRotation, maxRotation, 16);
            }
        }
    }
}


