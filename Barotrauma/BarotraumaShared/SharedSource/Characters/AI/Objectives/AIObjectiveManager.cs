﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        // TODO: expose
        public const float OrderPriority = 70;
        public const float RunPriority = 50;
        // Constantly increases the priority of the selected objective, unless overridden
        public const float baseDevotion = 5;

        /// <summary>
        /// Excluding the current order.
        /// </summary>
        public List<AIObjective> Objectives { get; private set; } = new List<AIObjective>();

        private readonly Character character;


        private float _waitTimer;
        /// <summary>
        /// When set above zero, the character will stand still doing nothing until the timer runs out. Does not affect orders, find safety or combat.
        /// </summary>
        public float WaitTimer
        {
            get { return _waitTimer; }
            set
            {
                _waitTimer = IsAllowedToWait() ? value : 0;
            }
        }

        public AIObjective CurrentOrder { get; private set; }
        public AIObjective CurrentObjective { get; private set; }

        public bool IsCurrentOrder<T>() where T : AIObjective => CurrentOrder is T;
        public bool IsCurrentObjective<T>() where T : AIObjective => CurrentObjective is T;
        public bool IsActiveObjective<T>() where T : AIObjective => GetActiveObjective() is T;

        public AIObjective GetActiveObjective() => CurrentObjective?.GetActiveObjective();
        /// <summary>
        /// Returns the last active objective of the specific type.
        /// </summary>
        public T GetActiveObjective<T>() where T : AIObjective => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).LastOrDefault(so => so is T) as T;

        /// <summary>
        /// Returns all active objectives of the specific type. Creates a new collection -> don't use too frequently.
        /// </summary>
        public IEnumerable<T> GetActiveObjectives<T>() where T : AIObjective => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).Where(so => so is T).Select(so => so as T);

        public bool HasActiveObjective<T>() where T : AIObjective => CurrentObjective is T || CurrentObjective != null && CurrentObjective.GetSubObjectivesRecursive().Any(so => so is T);

        public AIObjectiveManager(Character character)
        {
            this.character = character;
            CreateAutonomousObjectives();
        }

        public void AddObjective<T>(T objective) where T : AIObjective
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace);
#endif
                return;
            }
            // Can't use the generic type, because it's possible that the user of this method uses the base type AIObjective.
            // We need to get the highest type.
            var type = objective.GetType();
            if (objective.AllowMultipleInstances)
            {
                if (Objectives.FirstOrDefault(o => o.GetType() == type) is T existingObjective && existingObjective.IsDuplicate(objective))
                {
                    Objectives.Remove(existingObjective);
                }
            }
            else
            {
                Objectives.RemoveAll(o => o.GetType() == type);
            }
            Objectives.Add(objective);
        }

        public Dictionary<AIObjective, CoroutineHandle> DelayedObjectives { get; private set; } = new Dictionary<AIObjective, CoroutineHandle>();

        private void ClearIgnored()
        {
            if (character.AIController is HumanAIController humanAi)
            {
                humanAi.UnreachableHulls.Clear();
                humanAi.IgnoredItems.Clear();
            }
        }

        public void CreateAutonomousObjectives()
        {
            if (character.IsDead)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to create autonomous orders for a dead character");
#else
                return;
#endif
            }
            foreach (var delayedObjective in DelayedObjectives)
            {
                CoroutineManager.StopCoroutines(delayedObjective.Value);
            }
            DelayedObjectives.Clear();
            Objectives.Clear();
            AddObjective(new AIObjectiveFindSafety(character, this));
            AddObjective(new AIObjectiveIdle(character, this));
            int objectiveCount = Objectives.Count;
            foreach (var autonomousObjective in character.Info.Job.Prefab.AutonomousObjective)
            {
                var orderPrefab = Order.GetPrefab(autonomousObjective.identifier);
                if (orderPrefab == null) { throw new Exception($"Could not find a matching prefab by the identifier: '{autonomousObjective.identifier}'"); }
                var item = orderPrefab.MustSetTarget ? orderPrefab.GetMatchingItems(character.Submarine, false)?.GetRandom() : null;
                var order = new Order(orderPrefab, item ?? character.CurrentHull as Entity,
                    item?.Components.FirstOrDefault(ic => ic.GetType() == orderPrefab.ItemComponentType), orderGiver: character);
                if (order == null) { continue; }
                var objective = CreateObjective(order, autonomousObjective.option, character, isAutonomous: true, autonomousObjective.priorityModifier);
                if (objective != null && objective.CanBeCompleted)
                {
                    AddObjective(objective, delay: Rand.Value() / 2);
                    objectiveCount++;
                }
            }
            _waitTimer = Math.Max(_waitTimer, Rand.Range(0.5f, 1f) * objectiveCount);
        }

        public void AddObjective<T>(T objective, float delay, Action callback = null) where T : AIObjective
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError($"{character.Name}: Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace);
#endif
                return;
            }
            if (DelayedObjectives.TryGetValue(objective, out CoroutineHandle coroutine))
            {
                CoroutineManager.StopCoroutines(coroutine);
                DelayedObjectives.Remove(objective);
            }
            coroutine = CoroutineManager.InvokeAfter(() =>
            {
                //round ended before the coroutine finished
                if (GameMain.GameSession == null || Level.Loaded == null) { return; }
                DelayedObjectives.Remove(objective);
                AddObjective(objective);
                callback?.Invoke();
            }, delay);
            DelayedObjectives.Add(objective, coroutine);
        }

        public T GetObjective<T>() where T : AIObjective => Objectives.FirstOrDefault(o => o is T) as T;

        private AIObjective GetCurrentObjective()
        {
            var previousObjective = CurrentObjective;
            var firstObjective = Objectives.FirstOrDefault();
            if (CurrentOrder != null && firstObjective != null && CurrentOrder.Priority > firstObjective.Priority)
            {
                CurrentObjective = CurrentOrder;
            }
            else
            {
                CurrentObjective = firstObjective;
            }
            if (previousObjective != CurrentObjective)
            {
                previousObjective?.OnDeselected();
                CurrentObjective?.OnSelected();
                GetObjective<AIObjectiveIdle>().CalculatePriority(Math.Max(CurrentObjective.Priority - 10, 0));
            }
            return CurrentObjective;
        }

        public float GetCurrentPriority()
        {
            return CurrentObjective == null ? 0.0f : CurrentObjective.Priority;
        }

        public void UpdateObjectives(float deltaTime)
        {
            if (CurrentOrder != null)
            {
#if DEBUG
                // Note: don't automatically remove orders here. Removing orders needs to be done via dismissing.
                if (CurrentOrder.IsCompleted)
                {
                    DebugConsole.NewMessage($"{character.Name}: ORDER {CurrentOrder.DebugTag} IS COMPLETED. CURRENTLY ALL ORDERS SHOULD BE LOOPING.", Color.Red);
                }
                else if (!CurrentOrder.CanBeCompleted)
                {
                    DebugConsole.NewMessage($"{character.Name}: ORDER {CurrentOrder.DebugTag}, CANNOT BE COMPLETED.", Color.Red);
                }
#endif
                CurrentOrder.Update(deltaTime);
            }
            if (WaitTimer > 0)
            {
                WaitTimer -= deltaTime;
                return;
            }
            for (int i = 0; i < Objectives.Count; i++)
            {
                var objective = Objectives[i];
                if (objective.IsCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it is completed.", Color.LightGreen);
#endif
                    Objectives.Remove(objective);
                }
                else if (!objective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it cannot be completed.", Color.Red);
#endif
                    Objectives.Remove(objective);
                }
                else
                {
                    objective.Update(deltaTime);
                }
            }
            GetCurrentObjective();
        }

        public void SortObjectives()
        {
            CurrentOrder?.GetPriority();
            Objectives.ForEach(o => o.GetPriority());
            if (Objectives.Any())
            {
                Objectives.Sort((x, y) => y.Priority.CompareTo(x.Priority));
            }
            GetCurrentObjective()?.SortSubObjectives();
        }

        public void DoCurrentObjective(float deltaTime)
        {
            if (WaitTimer <= 0)
            {
                CurrentObjective?.TryComplete(deltaTime);
            }
            else
            {
                character.AIController.SteeringManager.Reset();
            }
        }

        public void SetOrder(AIObjective objective)
        {
            CurrentOrder = objective;
        }

        public void SetOrder(Order order, string option, Character orderGiver)
        {
            if (character.IsDead)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to set an order for a dead character");
#else
                return;
#endif
            }
            ClearIgnored();
            CurrentOrder = CreateObjective(order, option, orderGiver, isAutonomous: false);
            if (CurrentOrder == null)
            {
                // Recreate objectives, because some of them may be removed, if impossible to complete (e.g. due to path finding)
                CreateAutonomousObjectives();
            }
            else
            {
                CurrentOrder.Reset();
            }
        }

        public AIObjective CreateObjective(Order order, string option, Character orderGiver, bool isAutonomous, float priorityModifier = 1)
        {
            if (order == null) { return null; }
            AIObjective newObjective;
            switch (order.Identifier.ToLowerInvariant())
            {
                case "follow":
                    if (orderGiver == null) { return null; }
                    newObjective = new AIObjectiveGoTo(orderGiver, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        CloseEnough = 100,
                        AllowGoingOutside = true,
                        IgnoreIfTargetDead = true,
                        followControlledCharacter = orderGiver == character,
                        mimic = true,
                        DialogueIdentifier = "dialogcannotreachplace"
                    };
                    break;
                case "wait":
                    newObjective = new AIObjectiveGoTo(order.TargetEntity ?? character, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        AllowGoingOutside = character.CurrentHull == null
                    };
                    break;
                case "fixleaks":
                    newObjective = new AIObjectiveFixLeaks(character, this, priorityModifier: priorityModifier, prioritizedHull: order.TargetEntity as Hull);
                    break;
                case "chargebatteries":
                    newObjective = new AIObjectiveChargeBatteries(character, this, option, priorityModifier);
                    break;
                case "rescue":
                    newObjective = new AIObjectiveRescueAll(character, this, priorityModifier);
                    break;
                case "repairsystems":
                case "repairmechanical":
                case "repairelectrical":
                    newObjective = new AIObjectiveRepairItems(character, this, priorityModifier: priorityModifier, prioritizedItem: order.TargetEntity as Item)
                    {
                        RelevantSkill = order.AppropriateSkill,
                        RequireAdequateSkills = isAutonomous
                    };
                    break;
                case "pumpwater":
                    if (order.TargetItemComponent is Pump targetPump)
                    {
                        if (order.TargetItemComponent.Item.NonInteractable) { return null; }
                        newObjective = new AIObjectiveOperateItem(targetPump, character, this, option, false, priorityModifier: priorityModifier)
                        {
                            IsLoop = true,
                            Override = orderGiver != null && orderGiver.IsPlayer
                        };
                        // ItemComponent.AIOperate() returns false by default -> We'd have to set IsLoop = false and implement a custom override of AIOperate for the Pump.cs, 
                        // if we want that the bot just switches the pump on/off and continues doing something else.
                        // If we want that the bot does the objective and then forgets about it, I think we could do the same plus dismiss when the bot is done.
                    }
                    else
                    {
                        newObjective = new AIObjectivePumpWater(character, this, option, priorityModifier: priorityModifier);
                    }
                    break;
                case "extinguishfires":
                    newObjective = new AIObjectiveExtinguishFires(character, this, priorityModifier);
                    break;
                case "fightintruders":
                    newObjective = new AIObjectiveFightIntruders(character, this, priorityModifier);
                    break;
                case "steer":
                    var steering = (order?.TargetEntity as Item)?.GetComponent<Steering>();
                    if (steering != null) { steering.PosToMaintain = steering.Item.Submarine?.WorldPosition; }
                    if (order.TargetItemComponent == null) { return null; }
                    if (order.TargetItemComponent.Item.NonInteractable) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, option,
                        requireEquip: false, useController: order.UseController, controller: order.ConnectedController, priorityModifier: priorityModifier)
                    {
                        IsLoop = true,
                        // Don't override unless it's an order by a player
                        Override = orderGiver != null && orderGiver.IsPlayer
                    };
                    break;
                default:
                    if (order.TargetItemComponent == null) { return null; }
                    if (order.TargetItemComponent.Item.NonInteractable) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, option,
                        requireEquip: false, useController: order.UseController, controller: order.ConnectedController, priorityModifier: priorityModifier)
                    {
                        IsLoop = true,
                        // Don't override unless it's an order by a player
                        Override = orderGiver != null && orderGiver.IsPlayer
                    };
                    if (newObjective.Abandon) { return null; }
                    break;
            }
            return newObjective;
        }

        private void DismissSelf()
        {
#if CLIENT
            if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.IsSinglePlayer)
            {
                GameMain.GameSession?.CrewManager?.SetCharacterOrder(character, Order.GetPrefab("dismissed"), null, character);
            }
#else
            GameMain.Server?.SendOrderChatMessage(new OrderChatMessage(Order.GetPrefab("dismissed"), null, null, character, character));
#endif
        }

        private bool IsAllowedToWait()
        {
            if (CurrentOrder != null) { return false; }
            if (CurrentObjective is AIObjectiveCombat || CurrentObjective is AIObjectiveFindSafety) { return false; }
            if (character.AnimController.InWater) { return false; }
            if (character.IsClimbing) { return false; }
            if (character.AIController is HumanAIController humanAI)
            {
                if (humanAI.UnsafeHulls.Contains(character.CurrentHull)) { return false; }
            }
            if (AIObjectiveIdle.IsForbidden(character.CurrentHull)) { return false; }
            return true;
        }
    }
}
