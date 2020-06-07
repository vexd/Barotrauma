﻿using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    struct DeconstructItem
    {
        public readonly string ItemIdentifier;
        //minCondition does <= check, meaning that below or equeal to min condition will be skipped.
        public readonly float MinCondition;
        //maxCondition does > check, meaning that above this max the deconstruct item will be skipped.
        public readonly float MaxCondition;
        //Condition of item on creation
        public readonly float OutCondition;
        //should the condition of the deconstructed item be copied to the output items
        public readonly bool CopyCondition;

        public DeconstructItem(XElement element)
        {
            ItemIdentifier = element.GetAttributeString("identifier", "notfound");
            MinCondition = element.GetAttributeFloat("mincondition", -0.1f);
            MaxCondition = element.GetAttributeFloat("maxcondition", 1.0f);
            OutCondition = element.GetAttributeFloat("outcondition", 1.0f);
            CopyCondition = element.GetAttributeBool("copycondition", false);
        }
    }

    class FabricationRecipe
    {
        public class RequiredItem
        {
            public readonly ItemPrefab ItemPrefab;
            public int Amount;
            public readonly float MinCondition;
            public readonly bool UseCondition;

            public RequiredItem(ItemPrefab itemPrefab, int amount, float minCondition, bool useCondition)
            {
                ItemPrefab = itemPrefab;
                Amount = amount;
                MinCondition = minCondition;
                UseCondition = useCondition;
            }
        }

        public readonly ItemPrefab TargetItem;
        public readonly string DisplayName;
        public readonly List<RequiredItem> RequiredItems;
        public readonly string[] SuitableFabricatorIdentifiers;
        public readonly float RequiredTime;
        public readonly float OutCondition; //Percentage-based from 0 to 1
        public readonly List<Skill> RequiredSkills;

        public FabricationRecipe(XElement element, ItemPrefab itemPrefab)
        {
            TargetItem = itemPrefab;
            string displayName = element.GetAttributeString("displayname", "");
            DisplayName = string.IsNullOrEmpty(displayName) ? itemPrefab.Name : TextManager.Get($"DisplayName.{displayName}");

            SuitableFabricatorIdentifiers = element.GetAttributeStringArray("suitablefabricators", new string[0]);

            RequiredSkills = new List<Skill>();
            RequiredTime = element.GetAttributeFloat("requiredtime", 1.0f);
            OutCondition = element.GetAttributeFloat("outcondition", 1.0f);
            RequiredItems = new List<RequiredItem>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requiredskill":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + itemPrefab.Name + "! Use skill identifiers instead of names.");
                            continue;
                        }

                        RequiredSkills.Add(new Skill(
                            subElement.GetAttributeString("identifier", ""),
                            subElement.GetAttributeInt("level", 0)));
                        break;
                    case "item":
                    case "requireditem":
                        string requiredItemIdentifier = subElement.GetAttributeString("identifier", "");
                        if (string.IsNullOrWhiteSpace(requiredItemIdentifier))
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + itemPrefab.Name + "! One of the required items has no identifier.");
                            continue;
                        }

                        float minCondition = subElement.GetAttributeFloat("mincondition", 1.0f);
                        //Substract mincondition from required item's condition or delete it regardless?
                        bool useCondition = subElement.GetAttributeBool("usecondition", true);
                        int count = subElement.GetAttributeInt("count", 1);


                        ItemPrefab requiredItem = MapEntityPrefab.Find(null, requiredItemIdentifier.Trim()) as ItemPrefab;
                        if (requiredItem == null)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + itemPrefab.Name + "! Required item \"" + requiredItemIdentifier + "\" not found.");
                            continue;
                        }

                        var existing = RequiredItems.Find(r => r.ItemPrefab == requiredItem);
                        if (existing == null)
                        {
                            RequiredItems.Add(new RequiredItem(requiredItem, count, minCondition, useCondition));
                        }
                        else
                        {

                            RequiredItems.Remove(existing);
                            RequiredItems.Add(new RequiredItem(requiredItem, existing.Amount + count, minCondition, useCondition));
                        }

                        break;
                }
            }
        }
    }

    class PreferredContainer
    {
        public readonly HashSet<string> Primary = new HashSet<string>();
        public readonly HashSet<string> Secondary = new HashSet<string>();
        public float SpawnProbability { get; private set; }
        public int MinAmount { get; private set; }
        public int MaxAmount { get; private set; }

        public PreferredContainer(XElement element)
        {
            Primary = XMLExtensions.GetAttributeStringArray(element, "primary", new string[0]).ToHashSet();
            Secondary = XMLExtensions.GetAttributeStringArray(element, "secondary", new string[0]).ToHashSet();
            SpawnProbability = element.GetAttributeFloat("spawnprobability", 0.0f);
            MinAmount = element.GetAttributeInt("minamount", 0);
            MaxAmount = Math.Max(MinAmount, element.GetAttributeInt("maxamount", 0));

            if (element.Attribute("spawnprobability") == null)
            {
                //if spawn probability is not defined but amount is, assume the probability is 1
                if (MaxAmount > 0)
                {
                    SpawnProbability = 1.0f;
                } 
            }
            else if (element.Attribute("minamount") == null && element.Attribute("maxamount") == null)
            {
                //spawn probability defined but amount isn't, assume amount is 1
                MinAmount = MaxAmount = 1;
                SpawnProbability = element.GetAttributeFloat("spawnprobability", 0.0f);
            }
        }
    }

    partial class ItemPrefab : MapEntityPrefab
    {
        private string name;
        public override string Name { get { return name; } }

        public static readonly PrefabCollection<ItemPrefab> Prefabs = new PrefabCollection<ItemPrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
            Item.RemoveByPrefab(this);
        }

        //default size
        protected Vector2 size;                

        private float impactTolerance;

        private bool canSpriteFlipX, canSpriteFlipY;
        
        private Dictionary<string, PriceInfo> prices;

        /// <summary>
        /// Defines areas where the item can be interacted with. If RequireBodyInsideTrigger is set to true, the character
        /// has to be within the trigger to interact. If it's set to false, having the cursor within the trigger is enough.
        /// </summary>
        public List<Rectangle> Triggers;

        private List<XElement> fabricationRecipeElements = new List<XElement>();

        private readonly Dictionary<string, float> treatmentSuitability = new Dictionary<string, float>();

        /// <summary>
        /// Is this prefab overriding a prefab in another content package
        /// </summary>
        public bool IsOverride;

        public XElement ConfigElement
        {
            get;
            private set;
        }

        public List<DeconstructItem> DeconstructItems
        {
            get;
            private set;
        }

        public List<FabricationRecipe> FabricationRecipes
        {
            get;
            private set;
        }

        public float DeconstructTime
        {
            get;
            private set;
        }

        public bool AllowDeconstruct
        {
            get;
            private set;
        }

        //how close the Character has to be to the item to pick it up
        [Serialize(120.0f, false)]
        public float InteractDistance
        {
            get;
            private set;
        }

        // this can be used to allow items which are behind other items tp
        [Serialize(0.0f, false)]
        public float InteractPriority
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool InteractThroughWalls
        {
            get;
            private set;
        }

        [Serialize(false, false, description: "Hides the condition bar displayed at the bottom of the inventory slot the item is in.")]
        public bool HideConditionBar { get; set; }

        //if true and the item has trigger areas defined, characters need to be within the trigger to interact with the item
        //if false, trigger areas define areas that can be used to highlight the item
        [Serialize(true, false)]
        public bool RequireBodyInsideTrigger
        {
            get;
            private set;
        }

        //if true and the item has trigger areas defined, players can only highlight the item when the cursor is on the trigger
        [Serialize(false, false)]
        public bool RequireCursorInsideTrigger
        {
            get;
            private set;
        }

        //should the camera focus on the item when selected
        [Serialize(false, false)]
        public bool FocusOnSelected
        {
            get;
            private set;
        }

        //the amount of "camera offset" when selecting the construction
        [Serialize(0.0f, false)]
        public float OffsetOnSelected
        {
            get;
            private set;
        }

        [Serialize(100.0f, false)]
        public float Health
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool Indestructible
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DamagedByExplosions
        {
            get;
            private set;
        }

        [Serialize(1f, false)]
        public float ExplosionDamageMultiplier
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DamagedByProjectiles
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DamagedByMeleeWeapons
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DamagedByRepairTools
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DamagedByMonsters
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool FireProof
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool WaterProof
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float ImpactTolerance
        {
            get { return impactTolerance; }
            set { impactTolerance = Math.Max(value, 0.0f); }
        }

        [Serialize(0.0f, false)]
        public float SonarSize
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseInHealthInterface
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DisableItemUsageWhenSelected
        {
            get;
            private set;
        }

        [Serialize("", false)]        
        public string CargoContainerIdentifier
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseContainedSpriteColor
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool UseContainedInventoryIconColor
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool ShowContentsInTooltip { get; private set; }

        //Containers (by identifiers or tags) that this item should be placed in. These are preferences, which are not enforced.
        public List<PreferredContainer> PreferredContainers
        {
            get;
            private set;
        } = new List<PreferredContainer>();

        /// <summary>
        /// How likely it is for the item to spawn in a level of a given type.
        /// Key = name of the LevelGenerationParameters (empty string = default value)
        /// Value = commonness
        /// </summary>
        public Dictionary<string, float> LevelCommonness
        {
            get;
            private set;
        } = new Dictionary<string, float>();

        public bool CanSpriteFlipX
        {
            get { return canSpriteFlipX; }
        }

        public bool CanSpriteFlipY
        {
            get { return canSpriteFlipY; }
        }

        public Vector2 Size
        {
            get { return size; }
        }

        public bool CanBeBought
        {
            get { return prices != null && prices.Count > 0; }
        }

        public static void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }

        public static void LoadFromFile(ContentFile file)
        {
            DebugConsole.Log("*** " + file.Path + " ***");
            RemoveByFile(file.Path);

            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }

            var rootElement = doc.Root;
            switch (rootElement.Name.ToString().ToLowerInvariant())
            {
                case "item":
                    new ItemPrefab(rootElement, file.Path, false)
                    {
                        ContentPackage = file.ContentPackage
                    };
                    break;
                case "items":
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            var itemElement = element.GetChildElement("item");
                            if (itemElement != null)
                            {
                                new ItemPrefab(itemElement, file.Path, true)
                                {
                                    ContentPackage = file.ContentPackage,
                                    IsOverride = true
                                };
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Cannot find an item element from the children of the override element defined in {file.Path}");
                            }
                        }
                        else
                        {
                            new ItemPrefab(element, file.Path, false)
                            {
                                ContentPackage = file.ContentPackage
                            };
                        }
                    }
                    break;
                case "override":
                    var items = rootElement.GetChildElement("items");
                    if (items != null)
                    {
                        foreach (var element in items.Elements())
                        {
                            new ItemPrefab(element, file.Path, true)
                            {
                                ContentPackage = file.ContentPackage,
                                IsOverride = true
                            };
                        }
                    }
                    foreach (var element in rootElement.GetChildElements("item"))
                    {
                        new ItemPrefab(element, file.Path, true)
                        {
                            ContentPackage = file.ContentPackage
                        };
                    }
                    break;
                default:
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name.ToString()}' in {file.Path}");
                    break;
            }
        }

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            DebugConsole.Log("Loading item prefabs: ");

            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }

            //initialize item requirements for fabrication recipes
            //(has to be done after all the item prefabs have been loaded, because the 
            //recipe ingredients may not have been loaded yet when loading the prefab)
            InitFabricationRecipes();
        }

        public static void InitFabricationRecipes()
        {
            foreach (ItemPrefab itemPrefab in Prefabs)
            {
                itemPrefab.FabricationRecipes.Clear();
                foreach (XElement fabricationRecipe in itemPrefab.fabricationRecipeElements)
                {
                    itemPrefab.FabricationRecipes.Add(new FabricationRecipe(fabricationRecipe, itemPrefab));
                }
            }
        }

        public static string GenerateLegacyIdentifier(string name)
        {
            return "legacyitem_" + name.ToLowerInvariant().Replace(" ", "");
        }

        public ItemPrefab(XElement element, string filePath, bool allowOverriding)
        {
            FilePath = filePath;
            ConfigElement = element;

            originalName = element.GetAttributeString("name", "");
            name = originalName;
            identifier = element.GetAttributeString("identifier", "");

            if (!Enum.TryParse(element.GetAttributeString("category", "Misc"), true, out MapEntityCategory category))
            {
                category = MapEntityCategory.Misc;
            }
            Category = category;

            var parentType = element.Parent?.GetAttributeString("itemtype", "") ?? string.Empty;

            //nameidentifier can be used to make multiple items use the same names and descriptions
            string nameIdentifier = element.GetAttributeString("nameidentifier", "");

            if (string.IsNullOrEmpty(originalName))
            {
                if (string.IsNullOrEmpty(nameIdentifier))
                {
                    name = TextManager.Get("EntityName." + identifier, true) ?? string.Empty;
                }
                else
                {
                    name = TextManager.Get("EntityName." + nameIdentifier, true) ?? string.Empty;
                }
            }
            else if (Category.HasFlag(MapEntityCategory.Legacy))
            {
                // Legacy items use names as identifiers, so we have to define them in the xml. But we also want to support the translations. Therefore
                if (string.IsNullOrEmpty(nameIdentifier))
                {
                    name = TextManager.Get("EntityName." + identifier, true) ?? originalName;
                }
                else
                {
                    name = TextManager.Get("EntityName." + nameIdentifier, true) ?? originalName;
                }

                if (string.IsNullOrWhiteSpace(identifier))
                {
                    identifier = GenerateLegacyIdentifier(originalName);
                }
            }
            
            if (string.Equals(parentType, "wrecked", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(name))
                {
                    name = TextManager.GetWithVariable("wreckeditemformat", "[name]", name);
                }
            }
            
            if (string.IsNullOrEmpty(name))
            {
                DebugConsole.ThrowError($"Unnamed item ({identifier})in {filePath}!");
            }

            DebugConsole.Log("    " + name);

            Aliases = new HashSet<string>
                (element.GetAttributeStringArray("aliases", null, convertToLowerInvariant: true) ??
                element.GetAttributeStringArray("Aliases", new string[0], convertToLowerInvariant: true));
            Aliases.Add(originalName.ToLowerInvariant());
            
            Triggers            = new List<Rectangle>();
            DeconstructItems    = new List<DeconstructItem>();
            FabricationRecipes  = new List<FabricationRecipe>();
            DeconstructTime     = 1.0f;

            Tags = new HashSet<string>(element.GetAttributeStringArray("tags", new string[0], convertToLowerInvariant: true));
            if (!Tags.Any())
            {
                Tags = new HashSet<string>(element.GetAttributeStringArray("Tags", new string[0], convertToLowerInvariant: true));
            }

            if (element.Attribute("cargocontainername") != null)
            {
                DebugConsole.ThrowError("Error in item prefab \"" + name + "\" - cargo container should be configured using the item's identifier, not the name.");
            }

            SerializableProperty.DeserializeProperties(this, element);

            if (string.IsNullOrEmpty(Description))
            {
                if (string.IsNullOrEmpty(nameIdentifier))
                {
                    Description = TextManager.Get("EntityDescription." + identifier, true) ?? string.Empty;
                }
                else
                {
                    Description = TextManager.Get("EntityDescription." + nameIdentifier, true) ?? string.Empty;
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        string spriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            spriteFolder = Path.GetDirectoryName(filePath);
                        }

                        canSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        canSpriteFlipY = subElement.GetAttributeBool("canflipy", true);

                        sprite = new Sprite(subElement, spriteFolder, lazyLoad: true);
                        if (subElement.Attribute("sourcerect") == null)
                        {
                            DebugConsole.ThrowError("Warning - sprite sourcerect not configured for item \"" + Name + "\"!");
                        }
                        size = sprite.size;

                        if (subElement.Attribute("name") == null && !string.IsNullOrWhiteSpace(Name))
                        {
                            sprite.Name = Name;
                        }
                        sprite.EntityID = identifier;
                        break;
                    case "price":
                        string locationType = subElement.GetAttributeString("locationtype", "");
                        if (prices == null) prices = new Dictionary<string, PriceInfo>();
                        prices[locationType.ToLowerInvariant()] = new PriceInfo(subElement);
                        break;
#if CLIENT
                    case "inventoryicon":
                        string iconFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            iconFolder = Path.GetDirectoryName(filePath);
                        }
                        InventoryIcon = new Sprite(subElement, iconFolder, lazyLoad: true);
                        break;
                    case "brokensprite":
                        string brokenSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            brokenSpriteFolder = Path.GetDirectoryName(filePath);
                        }

                        var brokenSprite = new BrokenItemSprite(
                            new Sprite(subElement, brokenSpriteFolder, lazyLoad: true), 
                            subElement.GetAttributeFloat("maxcondition", 0.0f),
                            subElement.GetAttributeBool("fadein", false),
                            subElement.GetAttributePoint("offset", Point.Zero));

                        int spriteIndex = 0;
                        for (int i = 0; i < BrokenSprites.Count && BrokenSprites[i].MaxCondition < brokenSprite.MaxCondition; i++)
                        {
                            spriteIndex = i;
                        }
                        BrokenSprites.Insert(spriteIndex, brokenSprite);
                        break;
                    case "decorativesprite":
                        string decorativeSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            decorativeSpriteFolder = Path.GetDirectoryName(filePath);
                        }

                        int groupID = 0;
                        DecorativeSprite decorativeSprite = null;
                        if (subElement.Attribute("texture") == null)
                        {
                            groupID = subElement.GetAttributeInt("randomgroupid", 0);
                        }
                        else
                        {
                            decorativeSprite = new DecorativeSprite(subElement, decorativeSpriteFolder, lazyLoad: true);
                            DecorativeSprites.Add(decorativeSprite);
                            groupID = decorativeSprite.RandomGroupID;
                        }
                        if (!DecorativeSpriteGroups.ContainsKey(groupID))
                        {
                            DecorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                        }
                        DecorativeSpriteGroups[groupID].Add(decorativeSprite);

                        break;
                    case "containedsprite":
                        string containedSpriteFolder = "";
                        if (!subElement.GetAttributeString("texture", "").Contains("/"))
                        {
                            containedSpriteFolder = Path.GetDirectoryName(filePath);
                        }
                        var containedSprite = new ContainedItemSprite(subElement, containedSpriteFolder, lazyLoad: true);
                        if (containedSprite.Sprite != null)
                        {
                            ContainedSprites.Add(containedSprite);
                        }
                        break;
#endif
                    case "deconstruct":
                        DeconstructTime = subElement.GetAttributeFloat("time", 1.0f);
                        AllowDeconstruct = true;
                        foreach (XElement deconstructItem in subElement.Elements())
                        {
                            if (deconstructItem.Attribute("name") != null)
                            {
                                DebugConsole.ThrowError("Error in item config \"" + Name + "\" - use item identifiers instead of names to configure the deconstruct items.");
                                continue;
                            }

                            DeconstructItems.Add(new DeconstructItem(deconstructItem));
                        }

                        break;
                    case "fabricate":
                    case "fabricable":
                    case "fabricableitem":
                        fabricationRecipeElements.Add(subElement);
                        break;
                    case "preferredcontainer":
                        var preferredContainer = new PreferredContainer(subElement);
                        if (preferredContainer.Primary.Count == 0 && preferredContainer.Secondary.Count == 0)
                        {
                            DebugConsole.ThrowError($"Error in item prefab {Name}: preferred container has no preferences defined ({subElement.ToString()}).");
                        }
                        else
                        {
                            PreferredContainers.Add(preferredContainer);
                        }
                        break;
                    case "trigger":
                        Rectangle trigger = new Rectangle(0, 0, 10, 10)
                        {
                            X = subElement.GetAttributeInt("x", 0),
                            Y = subElement.GetAttributeInt("y", 0),
                            Width = subElement.GetAttributeInt("width", 0),
                            Height = subElement.GetAttributeInt("height", 0)
                        };

                        Triggers.Add(trigger);

                        break;
                    case "levelresource":
                        foreach (XElement levelCommonnessElement in subElement.Elements())
                        {
                            string levelName = levelCommonnessElement.GetAttributeString("levelname", "").ToLowerInvariant();
                            if (!LevelCommonness.ContainsKey(levelName))
                            {
                                LevelCommonness.Add(levelName, levelCommonnessElement.GetAttributeFloat("commonness", 0.0f));
                            }
                        }
                        break;
                    case "suitabletreatment":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in item prefab \"" + Name + "\" - suitable treatments should be defined using item identifiers, not item names.");
                        }

                        string treatmentIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();

                        float suitability = subElement.GetAttributeFloat("suitability", 0.0f);

                        treatmentSuitability.Add(treatmentIdentifier, suitability);
                        break;
                }
            }

            if (sprite == null)
            {
                DebugConsole.ThrowError("Item \"" + Name + "\" has no sprite!");
#if SERVER
                sprite = new Sprite("", Vector2.Zero);
                sprite.SourceRect = new Rectangle(0, 0, 32, 32);
#else
                sprite = new Sprite(TextureLoader.PlaceHolderTexture, null, null)
                {
                    Origin = TextureLoader.PlaceHolderTexture.Bounds.Size.ToVector2() / 2
                };
#endif
                size = sprite.size;
                sprite.EntityID = identifier;
            }
            
            if (string.IsNullOrEmpty(identifier))
            {
                DebugConsole.ThrowError(
                    "Item prefab \"" + name + "\" has no identifier. All item prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }

            AllowedLinks = element.GetAttributeStringArray("allowedlinks", new string[0], convertToLowerInvariant: true).ToList();

            Prefabs.Add(this, allowOverriding);
        }

        public float GetTreatmentSuitability(string treatmentIdentifier)
        {
            return treatmentSuitability.TryGetValue(treatmentIdentifier, out float suitability) ? suitability : 0.0f;
        }

        public PriceInfo GetPrice(Location location)
        {
            if (prices == null || !prices.ContainsKey(location.Type.Identifier.ToLowerInvariant())) { return null; }
            return prices[location.Type.Identifier.ToLowerInvariant()];
        }

        public static ItemPrefab Find(string name, string identifier)
        {
            ItemPrefab prefab;
            if (string.IsNullOrEmpty(identifier))
            {
                //legacy support
                identifier = GenerateLegacyIdentifier(name);
            }
            prefab = Find(p => p is ItemPrefab && p.Identifier==identifier) as ItemPrefab;

            //not found, see if we can find a prefab with a matching alias
            if (prefab == null && !string.IsNullOrEmpty(name))
            {
                string lowerCaseName = name.ToLowerInvariant();
                prefab = Prefabs.Find(me => me.Aliases != null && me.Aliases.Contains(lowerCaseName));
            }
            if (prefab == null)
            {
                prefab = Prefabs.Find(me => me.Aliases != null && me.Aliases.Contains(identifier));
            }

            if (prefab == null)
            {
                DebugConsole.ThrowError("Error loading item - item prefab \"" + name + "\" (identifier \"" + identifier + "\") not found.");
            }
            return prefab;
        }

        public IEnumerable<PriceInfo> GetPrices()
        {
            return prices?.Values;
        }

        public bool IsContainerPreferred(ItemContainer itemContainer, out bool isPreferencesDefined, out bool isSecondary)
        {
            isPreferencesDefined = PreferredContainers.Any();
            isSecondary = false;
            if (!isPreferencesDefined) { return true; }
            if (PreferredContainers.Any(pc => IsContainerPreferred(pc.Primary, itemContainer)))
            {
                return true;
            }
            isSecondary = true;
            return PreferredContainers.Any(pc => IsContainerPreferred(pc.Secondary, itemContainer));
        }

        public bool IsContainerPreferred(string[] identifiersOrTags, out bool isPreferencesDefined, out bool isSecondary)
        {
            isPreferencesDefined = PreferredContainers.Any();
            isSecondary = false;
            if (!isPreferencesDefined) { return true; }
            if (PreferredContainers.Any(pc => IsContainerPreferred(pc.Primary, identifiersOrTags)))
            {
                return true;
            }
            isSecondary = true;
            return PreferredContainers.Any(pc => IsContainerPreferred(pc.Secondary, identifiersOrTags));
        }

        public static bool IsContainerPreferred(IEnumerable<string> preferences, ItemContainer c) => preferences.Any(id => c.Item.Prefab.Identifier == id || c.Item.HasTag(id));
        public static bool IsContainerPreferred(IEnumerable<string> preferences, IEnumerable<string> ids) => ids.Any(id => preferences.Contains(id));
    }
}
