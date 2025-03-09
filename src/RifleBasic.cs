using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
// ReSharper disable ConditionIsAlwaysTrueOrFalse

namespace SmokeAndPowder
{
    public class SmokeAndPowderSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("ItemCartridge", typeof(ItemCartridge));
            api.RegisterItemClass("ItemRifle", typeof(ItemRifle));
        }
    }

    public class ItemCartridge : Item
    {
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack.Collectible.Attributes == null) return;

            var dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();
            if (dmg != 0) dsc.AppendLine((dmg > 0 ? "+" : "") + dmg + Lang.Get(" piercing-damage"));
            var shotCount = inSlot.Itemstack.Collectible.Attributes["projectileCount"].AsInt(1);
            dsc.AppendLine("fires " + shotCount + "x projectile(s)");

        }
    }

    public class ItemRifle : Item
    {
        private WorldInteraction[]? _interactions;

        private static SimpleParticleProperties? _smokeParticles;

        public override void OnLoaded(ICoreAPI coreApi)
        {
            if (coreApi.Side != EnumAppSide.Client) return;
            
            // Ugh this is going to be ugly
            _smokeParticles = new SimpleParticleProperties(Attributes["smokePNumMin"].AsFloat(9f), Attributes["smokePNumMax"].AsFloat(14f),
            ColorUtil.ToRgba(Attributes["smokePColorR"].AsInt(190), Attributes["smokePColorG"].AsInt(140), Attributes["smokePColorB"].AsInt(140),
            Attributes["smokePColorA"].AsInt(70)), new Vec3d(Attributes["smokePPosMinX"].AsFloat(-0.4f), Attributes["smokePPosMinY"].AsFloat(-0.4f), 
            Attributes["smokePPosMinZ"].AsFloat(-0.4f)), new Vec3d(Attributes["smokePPosMaxX"].AsFloat(0.4f), Attributes["smokePPosMaxY"].AsFloat(0.4f), 
            Attributes["smokePPosMaxZ"].AsFloat(0.4f)), new Vec3f(Attributes["smokePVelMinX"].AsFloat(-0.125f), Attributes["smokePVelMinY"].AsFloat(0.01f), 
            Attributes["smokePVelMinZ"].AsFloat(-0.125f)), new Vec3f(Attributes["smokePVelMaxX"].AsFloat(0.125f), Attributes["smokePVelMaxY"].AsFloat(0.3f), 
            Attributes["smokePVelMaxZ"].AsFloat(0.125f)), Attributes["smokePLifeTime"].AsFloat(2f), Attributes["smokePGrav"].AsFloat(-0.008f), 
            Attributes["smokePSizeMin"].AsFloat(1.0f), Attributes["smokePSizeMax"].AsFloat(1.9f), EnumParticleModel.Quad)
                {
                    SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, Attributes["smokePSizeEvolve"].AsFloat(-0.25f)),
                    SelfPropelled = true,
                    WindAffectednes = Attributes["smokePWindFactor"].AsFloat(0.7f),
                    OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, Attributes["smokePOpacityEvolve"].AsFloat(-0.25f))
                };

            _interactions = ObjectCacheUtil.GetOrCreate(coreApi, "rifleInteractions", () =>
            {
                List<ItemStack> stacks = new List<ItemStack>();

                foreach (var obj in coreApi.World.Collectibles)
                {
                    //if (obj.Code.Path.StartsWith("cartridge-") || obj.Code.Path.StartsWith("buckshot-"))
                    if (obj.Code.Path.StartsWith("cartridge-"))
                    {
                        stacks.Add(new ItemStack(obj));
                    }
                }

                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "heldhelp-chargebow",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    }
                };
            });
        }



        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity) => null!;

        private static ItemSlot? GetNextAmmoItem(EntityAgent byEntity, ItemSlot firearm)
        {
            ItemSlot? slot = null;
            byEntity.WalkInventory((invslot) =>
            {
                if (invslot is ItemSlotCreative){
                    byEntity.World.Logger.Debug("Found creative item slot in search for ammo by {0} for {1}",byEntity,firearm);
                    return true;
                }
                
                if (invslot.Itemstack != null && invslot.Itemstack.Collectible.Code.Path.StartsWith("cartridge-") )
                {
                    byEntity.World.Logger.Debug("Found an item beginning with cartridge- ({0}) in search for ammo to fit {1}",invslot.Itemstack.Collectible.Code.GetName(),firearm);
                    slot = invslot;
                    return false;
                }
                return true;
            });

            return slot;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            var invslot = GetNextAmmoItem(byEntity, slot);
            if (invslot == null) return;

            if (byEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.TempAttributes.SetInt("renderVariant", 0);
            }

            slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();

            // Not ideal to code the aiming controls this way. Needs an elegant solution - maybe an event bus?
            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            //byEntity.AnimManager.StartAnimation("bowaim");

            IPlayer? byPlayer = null;
            if (Attributes["startAimSound"].AsString("").Length > 0)
			{
                if (byEntity is EntityPlayer entityPlayer) byPlayer = entityPlayer.World.PlayerByUid(entityPlayer.PlayerUID);
                var soundLocation = AssetLocation.Create(Attributes["startAimSound"].AsString(""), Code.Domain);
                byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer, false, 8);
            }

            handling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.AnimManager.StopAnimation("bowaim");

            if (byEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            }

            slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();

            if (cancelReason != EnumItemUseCancelReason.ReleasedMouse)
            {
                byEntity.Attributes.SetInt("aimingCancel", 1);
            }

            IPlayer? byPlayer = null;
            if (Attributes["cancelAimSound"].AsString("").Length <= 0) return true;
            if (byEntity is EntityPlayer entityPlayer) byPlayer = entityPlayer.World.PlayerByUid(entityPlayer.PlayerUID);
            var soundLocation = AssetLocation.Create(Attributes["cancelAimSound"].AsString(""), Code.Domain);
            byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer, false, 12);

            return true;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (byEntity.Attributes.GetInt("aimingCancel") == 1) return;
            byEntity.Attributes.SetInt("aiming", 0);
            //byEntity.AnimManager.StopAnimation("bowaim");

            if (byEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            }
            
            //byEntity.World.Logger.Debug("{0} stopping held interaction with ItemRifle, held for {1} seconds!", byEntity, secondsUsed);
            slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();

            var loadingTime = Attributes["loadingTime"].AsFloat(0.5f);
            if (secondsUsed < loadingTime)
            { 
                //byEntity.World.Logger.Debug("{0} seconds was smaller than the loading time of {1} seconds, cancelling interaction...", secondsUsed, loadingTime);
                if (Attributes["stopAimSound"].AsString("").Length <= 0) return;
                IPlayer? byPlayer2 = null;
                if (byEntity is EntityPlayer entityPlayer) byPlayer2 = entityPlayer.World.PlayerByUid(entityPlayer.PlayerUID);
        
                var soundLocation = AssetLocation.Create(Attributes["stopAimSound"].AsString(""), Code.Domain);
                byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer2, false, 12);
                return;    
            }
            
            byEntity.World.Logger.Debug("Entity ({0}) is checking for more ammo to fit {1}",byEntity,slot);
            var ammoSlot = GetNextAmmoItem(byEntity, slot);
            if (ammoSlot == null) return;
            //byEntity.World.Logger.Debug("We have an ammo slot ({0}) let's check what type it is now...",ammoSlot);
            // var bulletMaterial = ammoSlot.Itemstack.Collectible.FirstCodePart(2);

            var damage = 1f;
            var projectileCount = ammoSlot.Itemstack.Collectible.Attributes["projectileCount"].AsInt(1);
            byEntity.World.Logger.Debug("damage initially {0}",damage);
            if (slot.Itemstack.Collectible.Attributes != null && projectileCount == 1)
            {
               // byEntity.World.Logger.Debug("Tool in slot {0} ({1}:{2}) has a damage value, damage was {2} and is now {3}", slot, slot.Itemstack.Collectible.Code.Domain, slot.Itemstack.Collectible.Code, damage, damage + slot.Itemstack.Collectible.Attributes["damage"].AsFloat(0));
                damage += slot.Itemstack.Collectible.Attributes["damage"].AsFloat();
                byEntity.World.Logger.Debug("single projectile damage increased to {0}",damage);
            }
            // Only apply the projectile's bonus damage to multiple-projectile ammo types, this is usually something like 0.5-2.5 damage
            if (ammoSlot.Itemstack.Collectible.Attributes != null)
            {
                damage += ammoSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();
                byEntity.World.Logger.Debug("damage increased to {0}",damage);
                //byEntity.World.Logger.Debug("Ammo in slot {0} ({1}:{2}) has a damage value, damage was {2} and is now {3}", ammoSlot, ammoSlot.Itemstack.Collectible.Code.Domain, ammoSlot.Itemstack.Collectible.Code, damage, damage + ammoSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0));
            }
            byEntity.World.Logger.Debug("damage total {0}",damage);
            var stack = ammoSlot.TakeOut(1);
            byEntity.World.Logger.Debug("ammo slot {0}, ammo stack {1}",ammoSlot.ToString(),stack.ToString());
            byEntity.World.Logger.Debug("ammo stack attributes: {0}",stack.Item.Attributes);
            ammoSlot.MarkDirty();

            byEntity.World.Logger.Debug("We have an ammo slot ({0}) let's check what type it is now...",stack);
            IPlayer? byPlayer = null;
            byEntity.World.Logger.Debug("aabbaabb 1");
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            byEntity.World.Logger.Debug("aabbaabb 2");
            if (Attributes["firingSound"].AsString("").Length > 0)
            {   
                byEntity.World.Logger.Debug("aabbaabb 3 playing sound by player");
                var firingSoundLocation = AssetLocation.Create(Attributes["firingSound"].AsString(""), Code.Domain);
                byEntity.World.PlaySoundAt(firingSoundLocation, byEntity, byPlayer, false, 32, 1.25f);
                byEntity.World.Logger.Debug("aabbaabb 4 done playing sound");
            }
            byEntity.World.Logger.Debug("aabbaabb 5 getting projectileEntityString");
            var projectileEntityString = stack.Collectible.Attributes["projectileEntity"].AsString("cartridgebullet-" + stack.Collectible.Variant["material"]);
            //string projectileEntityString = "cartridgebullet-" + stack.Collectible.Variant["material"];
            byEntity.World.Logger.Debug("Got entity entity string {0}",projectileEntityString);
            //Api.World.GetEntityType
            var projectileEntityLocation = new AssetLocation(projectileEntityString);
            byEntity.World.Logger.Debug("aabbaabb 6");

            // If we fail to find a valid projectile entity from either the ammo's projectileEntityString or the default then use a secondary fall-back option
            // In this case it's either the gun slot's projectilEntity
            if (projectileEntityLocation == null)
            {
                projectileEntityString =  slot.Itemstack.Collectible.Attributes?["projectileEntity"].AsString("cartridgebullet-lead");
            }
            byEntity.World.Logger.Debug("aabbaabb 7");
            projectileEntityLocation = AssetLocation.Create(projectileEntityString, Code.Domain);
            byEntity.World.Logger.Debug("Got projectileEntityLocation ({0}), domain is {1}",projectileEntityLocation,projectileEntityLocation.Domain);
            if (projectileEntityLocation == null)
            {
                byEntity.World.Logger.Debug("Failed to locate valid entity with projectileEntityString entries taken from multiple locations! Check validity of projectileEntity attributes on gun ({0}) and ammo ({1})!",slot.Itemstack.Collectible.Code,stack.Collectible.Code);
                return;
            }
			var projectileType = byEntity.World.GetEntityType(projectileEntityLocation);
            byEntity.World.Logger.Debug("Got projectile type now ({0})", projectileType.ToString());
            if (projectileType == null)
            {
                byEntity.World.Logger.Debug("Could not produce valid entity from AssetLocation ({0}) for some reason! Check validity of projectileEntity attributes on gun ({0}) and ammo ({1})!",slot.Itemstack.Collectible.Code,stack.Collectible.Code);
                return;
            }
            byEntity.World.Logger.Debug("now creating projectiles");
            // Hopefully keep the number of projectiles between 1 and 16, any more would be excessive
            var projectileNum = Math.Max(Math.Min(projectileCount, 16),1);
            if (projectileCount > 16) { 
                byEntity.World.Logger.Debug("Excessive number of projectiles (" + projectileCount + " > 16) defined by ammo item " + stack.Collectible.Code + "! Please use fewer projectiles!"); 
            }
            var rand = new Random((int)(byEntity.EntityId + byEntity.World.ElapsedMilliseconds));
            var acc = Math.Max(0.001f, (1 - byEntity.Attributes.GetFloat("aimingAccuracy")));
            var accModifier = Math.Max(0.25f,Attributes["accuracyModifier"].AsFloat(0.75f));
            double rndpitch;
            double rndyaw;
            // var projectileSpeedModifier = stack.Collectible.Attributes["projectileSpeedModifier"].AsFloat(1f);
            var projectileAccModifier = stack.Collectible.Attributes["projectileAccModifier"].AsFloat(1f);
            Entity? projectileEntity = null;
            Vec3d? entityPos;
            Vec3d? entityVelocity;
            byEntity.World.Logger.Debug("Making {0} projectiles of now...", projectileCount);
            for (var px = 0; px < projectileNum; px++)
            {
			    projectileEntity = byEntity.World.ClassRegistry.CreateEntity(projectileType);
			    if (projectileEntity is EntityProjectile entProjectile)
			    {
                    //byEntity.World.Logger.Debug("Our entity is an EntityProjectile!",projectileEntity.ToString());
			    	entProjectile.FiredBy = byEntity;
			    	entProjectile.Damage = damage;
			    	entProjectile.ProjectileStack = stack;
                    entProjectile.DamageStackOnImpact = true;
			    	entProjectile.DropOnImpactChance = 0f;
                    byEntity.World.Logger.Debug("Stats set for projectile {0}: firedby: {1}, damage: {2}, projectilestack: {3}, damageonimpact: {4}, dropchance: {5}", px,byEntity,damage,stack,true,0f);
			    }

                rndpitch = (rand.NextDouble() - 0.5) * acc * accModifier * projectileAccModifier;
                rndyaw = (rand.NextDouble() - 0.5) * acc * accModifier * projectileAccModifier;
                byEntity.World.Logger.Debug("Projectile {0} pitch/yaw: {1}/{2}",px,rndpitch,rndyaw);

                //byEntity.World.Logger.Debug("We've made the entity {0}!",projectileEntity.ToString());
                //byEntity.World.Logger.Debug("Did byEntity aiming attributes changes {0}/{1}/{2}",acc, rndpitch, rndyaw);
            
                entityPos = byEntity.ServerPos.XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0);
                //byEntity.World.Logger.Debug("Got entityPos ({0})",entityPos);
			    entityVelocity = (entityPos.AheadCopy(1.0, byEntity.SidedPos.Pitch + rndpitch, byEntity.SidedPos.Yaw + rndyaw) - entityPos) * Attributes["projectileSpeed"].AsFloat(1f);
			    //byEntity.World.Logger.Debug("Got entityVelocity ({0})",entityVelocity);
                projectileEntity.ServerPos.SetPos(byEntity.SidedPos.BehindCopy(0.21).XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0));
                //byEntity.World.Logger.Debug("Set the projectileEntity's position ({0})",projectileEntity.ServerPos);
			    projectileEntity.ServerPos.Motion.Set(entityVelocity);
                byEntity.World.Logger.Debug("Projectile {0} pos and motion vector: {1}/{2}",px,projectileEntity.ServerPos.XYZ,projectileEntity.ServerPos.Motion);
                //byEntity.World.Logger.Debug("Got entityVelocity ({0})",entityVelocity);

                projectileEntity.Pos.SetFrom(projectileEntity.ServerPos);
                byEntity.World.Logger.Debug("Projectile {0} spawned!",px);
                byEntity.World.SpawnEntity(projectileEntity);

            }

            if (_smokeParticles != null)
            {
                _smokeParticles.MinPos = byEntity.SidedPos.AheadCopy(Attributes["smokePVelFOffset"].AsDouble(2.0)).XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0);
			    byEntity.World.SpawnParticles(_smokeParticles);
            }
			
            //byEntity.World.Logger.Debug("Set projectileEntity's client position ({0})",projectileEntity.Pos);
            if (projectileEntity != null)
			    projectileEntity.World = byEntity.World;
			if (projectileEntity is EntityProjectile entityProjectileHere)
            {
				entityProjectileHere.SetRotation();
                //byEntity.World.Logger.Debug("ProjectileEntity was a projectile, setting rotation");
			}

            //byEntity.World.Logger.Debug("Finally spawned the projectile entity");
			
            //slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, ammoSlot.Itemstack.Collectible.Attributes["durabilityDamageAmount"].AsInt(1));
            slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, stack.Collectible.Attributes["durabilityDamageAmount"].AsInt(1));
            
            slot.Itemstack.Attributes.SetBool("loaded", false);
            //byEntity.World.Logger.Debug("Damaged tool/weapon in slot ({0}/{1})",slot,slot.Itemstack.Collectible);
			byEntity.AnimManager.StartAnimation(Attributes["hitAnimation"].AsString("bowhit"));

        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent interactingEntity, BlockSelection blockSel, EntitySelection entitySel)
		{
            var loadingTime = Attributes["loadingTime"].AsFloat(0.5f);
            //if (secondsUsed < loadingTime && (!slot.Itemstack.TempAttributes.HasAttribute("renderVariant") || slot.Itemstack.TempAttributes.GetInt("renderVariant") != 1))
            if (!(secondsUsed > loadingTime) || slot.Itemstack.Attributes.GetBool("loaded")) return true;
            if (interactingEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.TempAttributes.SetInt("renderVariant", 1);
            }

            slot.Itemstack.Attributes.SetInt("renderVariant", 1);
            (interactingEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
 
            slot.MarkDirty();
            //interactingEntity.World.Logger.Debug("Entity is a player, broadcasting hotbar!");

            slot.Itemstack.Attributes.SetBool("loaded", true);
            //interactingEntity.World.Logger.Debug("Doing ItemRifle loading, secondsUsed > loading time ({0} > {1}), isLoaded is {2} and (temp/permenant) renderVariant is now {3} and {4}", secondsUsed, loadingTime, slot.Itemstack.Attributes.GetBool("loaded", false), slot.Itemstack.TempAttributes.GetInt("renderVariant"), slot.Itemstack.Attributes.GetInt("renderVariant") );

            if (Attributes["readyAimSound"].AsString("").Length <= 0) return true;
            IPlayer? byPlayer = null;
            if (interactingEntity is EntityPlayer) byPlayer = interactingEntity.World.PlayerByUid(((EntityPlayer)interactingEntity).PlayerUID);
            var soundLocation = AssetLocation.Create(Attributes["readyAimSound"].AsString(""), Code.Domain);
            interactingEntity.World.PlaySoundAt(soundLocation, interactingEntity, byPlayer, false, 16);
            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack.Collectible.Attributes == null) return;

            var dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();
            if (dmg != 0) dsc.AppendLine(dmg + Lang.Get(" piercing-damage"));
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return _interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }

    }
}
