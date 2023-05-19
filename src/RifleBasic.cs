using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

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

            float dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);
            if (dmg != 0) dsc.AppendLine((dmg > 0 ? "+" : "") + dmg + Lang.Get("piercing-damage"));
            int shotCount = inSlot.Itemstack.Collectible.Attributes["projectileCount"].AsInt(1);
            dsc.AppendLine("fires " + shotCount + "x projectile(s)");

        }
    }

    public class ItemRifle : Item
    {
        WorldInteraction[] interactions;

        public static SimpleParticleProperties smokeParticles;

        string cartridgePrefix;

        string altCartridgePrefix; 

        static ItemRifle()
        {
            /*
            ItemRifle.smokeParticles = new SimpleParticleProperties(9f, 14f, ColorUtil.ToRgba(180, 110, 110, 80), new Vec3d(-0.3f, -0.3f, -0.3f),
            new Vec3d(0.3f, 0.3f, 0.3f), new Vec3f(-0.125f, 0.01f, -0.125f),
            new Vec3f(0.125f, 0.3f, 0.125f), 2f, -0.008f, 1.0f, 1.9f, EnumParticleModel.Quad);
            ItemRifle.smokeParticles.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.25f);
		    ItemRifle.smokeParticles.SelfPropelled = true;
            ItemRifle.smokeParticles.WindAffectednes = 0.7f;
            ItemRifle.smokeParticles.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.25f);
            */
        }	

        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;
            
            // Ugh this is going to be ugly
            ItemRifle.smokeParticles = new SimpleParticleProperties(this.Attributes["smokePNumMin"].AsFloat(9f), this.Attributes["smokePNumMax"].AsFloat(14f),
            ColorUtil.ToRgba(this.Attributes["smokePColorR"].AsInt(190), this.Attributes["smokePColorG"].AsInt(140), this.Attributes["smokePColorB"].AsInt(140),
            this.Attributes["smokePColorA"].AsInt(70)), new Vec3d(this.Attributes["smokePPosMinX"].AsFloat(-0.4f), this.Attributes["smokePPosMinY"].AsFloat(-0.4f), 
            this.Attributes["smokePPosMinZ"].AsFloat(-0.4f)), new Vec3d(this.Attributes["smokePPosMaxX"].AsFloat(0.4f), this.Attributes["smokePPosMaxY"].AsFloat(0.4f), 
            this.Attributes["smokePPosMaxZ"].AsFloat(0.4f)), new Vec3f(this.Attributes["smokePVelMinX"].AsFloat(-0.125f), this.Attributes["smokePVelMinY"].AsFloat(0.01f), 
            this.Attributes["smokePVelMinZ"].AsFloat(-0.125f)), new Vec3f(this.Attributes["smokePVelMaxX"].AsFloat(0.125f), this.Attributes["smokePVelMaxY"].AsFloat(0.3f), 
            this.Attributes["smokePVelMaxZ"].AsFloat(0.125f)), this.Attributes["smokePLifeTime"].AsFloat(2f), this.Attributes["smokePGrav"].AsFloat(-0.008f), 
            this.Attributes["smokePSizeMin"].AsFloat(1.0f), this.Attributes["smokePSizeMax"].AsFloat(1.9f), EnumParticleModel.Quad);
            ItemRifle.smokeParticles.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, this.Attributes["smokePSizeEvolve"].AsFloat(-0.25f));
		    ItemRifle.smokeParticles.SelfPropelled = true;
            ItemRifle.smokeParticles.WindAffectednes = this.Attributes["smokePWindFactor"].AsFloat(0.7f);
            ItemRifle.smokeParticles.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, this.Attributes["smokePOpacityEvolve"].AsFloat(-0.25f));

            interactions = ObjectCacheUtil.GetOrCreate(api, "rifleInteractions", () =>
            {
                List<ItemStack> stacks = new List<ItemStack>();

                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    //if (obj.Code.Path.StartsWith("cartridge-") || obj.Code.Path.StartsWith("buckshot-"))
                    if (obj.Code.Path.StartsWith("cartridge-"))
                    {
                        stacks.Add(new ItemStack(obj));
                    }
                }

                return new WorldInteraction[]
                {
                    new WorldInteraction()
                    {
                        ActionLangCode = "heldhelp-chargebow",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    }
                };
            });
        }



        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
        {
            return null;
        }

        ItemSlot GetNextAmmoItem(EntityAgent byEntity, ItemSlot firearm)
        {
            ItemSlot slot = null;
            byEntity.WalkInventory((invslot) =>
            {
                if (invslot is ItemSlotCreative){
                    byEntity.World.Logger.Error("Found creative item slot in search for ammo by {0} for {1}",byEntity,firearm);
                    return true;
                }
                
                if (invslot.Itemstack != null && invslot.Itemstack.Collectible.Code.Path.StartsWith("cartridge-") )
                {
                    byEntity.World.Logger.Error("Found an item beginning with cartridge- ({0}) in search for ammo to fit {1}",invslot.Itemstack.Collectible.Code.GetName(),firearm);
                    slot = invslot;
                    return false;
                }
                return true;
            });

            return slot;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            ItemSlot invslot = GetNextAmmoItem(byEntity, slot);
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

            IPlayer byPlayer = null;
            if (this.Attributes["startAimSound"].AsString("").Length > 0)
			{
                if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
                AssetLocation soundLocation = AssetLocation.Create(this.Attributes["startAimSound"].AsString(""), this.Code.Domain);
                byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer, false, 8);
            }

            handling = EnumHandHandling.PreventDefault;
        }


        /*
        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
//            if (byEntity.World is IClientWorldAccessor)
            {
                int renderVariant = GameMath.Clamp((int)Math.Ceiling(secondsUsed * 4), 0, 3);
                int prevRenderVariant = slot.Itemstack.Attributes.GetInt("renderVariant", 0);

                slot.Itemstack.TempAttributes.SetInt("renderVariant", renderVariant);
                slot.Itemstack.Attributes.SetInt("renderVariant", renderVariant);

                if (prevRenderVariant != renderVariant)
                {
                    (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
                }
            }

            
            return true;
        }
        */


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

            IPlayer byPlayer = null;
            if (this.Attributes["cancelAimSound"].AsString("").Length > 0)
			{
                if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
                AssetLocation soundLocation = AssetLocation.Create(this.Attributes["cancelAimSound"].AsString(""), this.Code.Domain);
                byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer, false, 12);
            }

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
            
            //byEntity.World.Logger.Error("{0} stopping held interaction with ItemRifle, held for {1} seconds!", byEntity, secondsUsed);
            slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();

            float loadingTime = this.Attributes["loadingTime"].AsFloat(0.5f);
            if (secondsUsed < loadingTime)
            { 
                //byEntity.World.Logger.Error("{0} seconds was smaller than the loading time of {1} seconds, cancelling interaction...", secondsUsed, loadingTime);
                if (this.Attributes["stopAimSound"].AsString("").Length > 0)
			    {
                    IPlayer byPlayer2 = null;
                    if (byEntity is EntityPlayer) byPlayer2 = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
        
                    AssetLocation soundLocation = AssetLocation.Create(this.Attributes["stopAimSound"].AsString(""), this.Code.Domain);
                    byEntity.World.PlaySoundAt(soundLocation, byEntity, byPlayer2, false, 12);
                }
                return;    
            }
            
            byEntity.World.Logger.Error("Entity ({0}) is checking for more ammo to fit {1}",byEntity,slot);
            ItemSlot ammoSlot = GetNextAmmoItem(byEntity, slot);
            if (ammoSlot == null) return;
            //byEntity.World.Logger.Error("We have an ammo slot ({0}) let's check what type it is now...",ammoSlot);
            string bulletMaterial = ammoSlot.Itemstack.Collectible.FirstCodePart(2);

            float damage = 1f;
            int projectileCount = ammoSlot.Itemstack.Collectible.Attributes["projectileCount"].AsInt(1);
            byEntity.World.Logger.Error("damage initially {0}",damage);
            if (slot.Itemstack.Collectible.Attributes != null && projectileCount == 1)
            {
               // byEntity.World.Logger.Error("Tool in slot {0} ({1}:{2}) has a damage value, damage was {2} and is now {3}", slot, slot.Itemstack.Collectible.Code.Domain, slot.Itemstack.Collectible.Code, damage, damage + slot.Itemstack.Collectible.Attributes["damage"].AsFloat(0));
                damage += slot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);
                byEntity.World.Logger.Error("single projectile damage increased to {0}",damage);
            }
            // Only apply the projectile's bonus damage to multiple-projectile ammo types, this is usually something like 0.5-2.5 damage
            if (ammoSlot.Itemstack.Collectible.Attributes != null)
            {
                damage += ammoSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);
                byEntity.World.Logger.Error("damage increased to {0}",damage);
                //byEntity.World.Logger.Error("Ammo in slot {0} ({1}:{2}) has a damage value, damage was {2} and is now {3}", ammoSlot, ammoSlot.Itemstack.Collectible.Code.Domain, ammoSlot.Itemstack.Collectible.Code, damage, damage + ammoSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0));
            }
            byEntity.World.Logger.Error("damage total {0}",damage);
            ItemStack stack = ammoSlot.TakeOut(1);
            byEntity.World.Logger.Error("ammo slot {0}, ammo stack {1}",ammoSlot.ToString(),stack.ToString());
            byEntity.World.Logger.Error("ammo stack attributes: {0}",stack.Item.Attributes);
            ammoSlot.MarkDirty();

            byEntity.World.Logger.Error("We have an ammo slot ({0}) let's check what type it is now...",stack);
            IPlayer byPlayer = null;
            byEntity.World.Logger.Error("aabbaabb 1");
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            byEntity.World.Logger.Error("aabbaabb 2");
            if (this.Attributes["firingSound"].AsString("").Length > 0)
            {   
                byEntity.World.Logger.Error("aabbaabb 3 playing sound by player");
                AssetLocation firingSoundLocation = AssetLocation.Create(this.Attributes["firingSound"].AsString(""), this.Code.Domain);
                byEntity.World.PlaySoundAt(firingSoundLocation, byEntity, byPlayer, false, 32, 1.25f);
                byEntity.World.Logger.Error("aabbaabb 4 done playing sound");
            }
            byEntity.World.Logger.Error("aabbaabb 5 getting projectileEntityString");
            string projectileEntityString = stack.Collectible.Attributes["projectileEntity"].AsString("cartridgebullet-" + stack.Collectible.Variant["material"]);
            //string projectileEntityString = "cartridgebullet-" + stack.Collectible.Variant["material"];
            byEntity.World.Logger.Error("Got entity entity string {0}",projectileEntityString);
            //Api.World.GetEntityType
            AssetLocation projectileEntityLocation = new AssetLocation(projectileEntityString);
            byEntity.World.Logger.Error("aabbaabb 6");

            // If we fail to find a valid projectile entity from either the ammo's projectileEntityString or the default then use a secondary fall-back option
            // In this case it's either the gun slot's projectilEntity
            if (projectileEntityLocation == null)
            {
                projectileEntityString =  slot.Itemstack.Collectible.Attributes["projectileEntity"].AsString("cartridgebullet-lead");
            }
            byEntity.World.Logger.Error("aabbaabb 7");
            projectileEntityLocation = AssetLocation.Create(projectileEntityString, this.Code.Domain);
            byEntity.World.Logger.Error("Got projectileEntityLocation ({0}), domain is {1}",projectileEntityLocation,projectileEntityLocation.Domain);
            if (projectileEntityLocation == null)
            {
                byEntity.World.Logger.Error("Failed to locate valid entity with projectileEntityString entries taken from multiple locations! Check validity of projectileEntity attributes on gun ({0}) and ammo ({1})!",slot.Itemstack.Collectible.Code,stack.Collectible.Code);
                return;
            }
			EntityProperties projectileType = byEntity.World.GetEntityType(projectileEntityLocation);
            byEntity.World.Logger.Error("Got projectile type now ({0})", projectileType.ToString());
            if (projectileType == null)
            {
                byEntity.World.Logger.Error("Could not produce valid entity from AssetLocation ({0}) for some reason! Check validity of projectileEntity attributes on gun ({0}) and ammo ({1})!",slot.Itemstack.Collectible.Code,stack.Collectible.Code);
                return;
            }
            byEntity.World.Logger.Error("now creating projectiles");
            // Hopefully keep the number of projectiles between 1 and 16, any more would be excessive
            int projectileNum = Math.Max(Math.Min(projectileCount, 16),1);
            if (projectileCount > 16) { 
                byEntity.World.Logger.Error("Excessive number of projectiles (" + projectileCount + " > 16) defined by ammo item " + stack.Collectible.Code + "! Please use fewer projectiles!"); 
            }
            Random Rand = new Random((int)(byEntity.EntityId + byEntity.World.ElapsedMilliseconds));
            float acc = Math.Max(0.001f, (1 - byEntity.Attributes.GetFloat("aimingAccuracy", 0)));
            float accModifier = Math.Max(0.25f,this.Attributes["accuracyModifier"].AsFloat(0.75f));
            double rndpitch = 0.5;
            double rndyaw = 0.5;
            float projectileSpeedModifier = stack.Collectible.Attributes["projectileSpeedModifier"].AsFloat(1f);
            float projectileAccModifier = stack.Collectible.Attributes["projectileAccModifier"].AsFloat(1f);
            Entity projectileEntity = null;
            Vec3d entityPos = null;
            Vec3d entityVelocity = null;
            byEntity.World.Logger.Error("Making {0} projectiles of now...", projectileCount);
            for (int px = 0; px < projectileNum; px++)
            {
			    projectileEntity = byEntity.World.ClassRegistry.CreateEntity(projectileType);
			    if (projectileEntity is EntityProjectile)
			    {
                    //byEntity.World.Logger.Error("Our entity is an EntityProjectile!",projectileEntity.ToString());
			    	((EntityProjectile)projectileEntity).FiredBy = byEntity;
			    	((EntityProjectile)projectileEntity).Damage = damage;
			    	((EntityProjectile)projectileEntity).ProjectileStack = stack;
                    ((EntityProjectile)projectileEntity).DamageStackOnImpact = true;
			    	((EntityProjectile)projectileEntity).DropOnImpactChance = 0f;
                    byEntity.World.Logger.Error("Stats set for projectile {0}: firedby: {1}, damage: {2}, projectilestack: {3}, damageonimpact: {4}, dropchance: {5}", px,byEntity,damage,stack,true,0f);
			    }

                rndpitch = (Rand.NextDouble() - 0.5) * acc * accModifier * projectileAccModifier;
                rndyaw = (Rand.NextDouble() - 0.5) * acc * accModifier * projectileAccModifier;
                byEntity.World.Logger.Error("Projectile {0} pitch/yaw: {1}/{2}",px,rndpitch,rndyaw);

                //byEntity.World.Logger.Error("We've made the entity {0}!",projectileEntity.ToString());
                //byEntity.World.Logger.Error("Did byEntity aiming attributes changes {0}/{1}/{2}",acc, rndpitch, rndyaw);
            
                entityPos = byEntity.ServerPos.XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0);
                //byEntity.World.Logger.Error("Got entityPos ({0})",entityPos);
			    entityVelocity = (entityPos.AheadCopy(1.0, (double)byEntity.SidedPos.Pitch + rndpitch, (double)byEntity.SidedPos.Yaw + rndyaw) - entityPos) * this.Attributes["projectileSpeed"].AsFloat(1f);
			    //byEntity.World.Logger.Error("Got entityVelocity ({0})",entityVelocity);
                projectileEntity.ServerPos.SetPos(byEntity.SidedPos.BehindCopy(0.21).XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0));
                //byEntity.World.Logger.Error("Set the projectileEntity's position ({0})",projectileEntity.ServerPos);
			    projectileEntity.ServerPos.Motion.Set(entityVelocity);
                byEntity.World.Logger.Error("Projectile {0} pos and motion vector: {1}/{2}",px,projectileEntity.ServerPos.XYZ,projectileEntity.ServerPos.Motion);
                //byEntity.World.Logger.Error("Got entityVelocity ({0})",entityVelocity);

                projectileEntity.Pos.SetFrom(projectileEntity.ServerPos);
                byEntity.World.Logger.Error("Projectile {0} spawned!",px);
                byEntity.World.SpawnEntity(projectileEntity);

            }

			ItemRifle.smokeParticles.MinPos = byEntity.SidedPos.AheadCopy(this.Attributes["smokePVelFOffset"].AsDouble(2.0)).XYZ.Add(0.0, byEntity.LocalEyePos.Y, 0.0);
			byEntity.World.SpawnParticles(ItemRifle.smokeParticles, null);
			
            //byEntity.World.Logger.Error("Set projectileEntity's client position ({0})",projectileEntity.Pos);
			projectileEntity.World = byEntity.World;
			if (projectileEntity is EntityProjectile)
            {
				((EntityProjectile)projectileEntity).SetRotation();
                //byEntity.World.Logger.Error("ProjectileEntity was a projectile, setting rotation");
			}

            //byEntity.World.Logger.Error("Finally spawned the projectile entity");
			
            //slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, ammoSlot.Itemstack.Collectible.Attributes["durabilityDamageAmount"].AsInt(1));
            slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot, stack.Collectible.Attributes["durabilityDamageAmount"].AsInt(1));
            
            slot.Itemstack.Attributes.SetBool("loaded", false);
            //byEntity.World.Logger.Error("Damaged tool/weapon in slot ({0}/{1})",slot,slot.Itemstack.Collectible);
			byEntity.AnimManager.StartAnimation(this.Attributes["hitAnimation"].AsString("bowhit"));

        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent interactingEntity, BlockSelection blockSel, EntitySelection entitySel)
		{
            float loadingTime = this.Attributes["loadingTime"].AsFloat(0.5f);
            //if (secondsUsed < loadingTime && (!slot.Itemstack.TempAttributes.HasAttribute("renderVariant") || slot.Itemstack.TempAttributes.GetInt("renderVariant") != 1))
            if (secondsUsed > loadingTime && !slot.Itemstack.Attributes.GetBool("loaded", false))
            {

				if (interactingEntity.World is IClientWorldAccessor)
                {
                    slot.Itemstack.TempAttributes.SetInt("renderVariant", 1);
                }

                slot.Itemstack.Attributes.SetInt("renderVariant", 1);
                (interactingEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
 
                slot.MarkDirty();
                //interactingEntity.World.Logger.Error("Entity is a player, broadcasting hotbar!");

                slot.Itemstack.Attributes.SetBool("loaded", true);
                //interactingEntity.World.Logger.Error("Doing ItemRifle loading, secondsUsed > loading time ({0} > {1}), isLoaded is {2} and (temp/permenant) renderVariant is now {3} and {4}", secondsUsed, loadingTime, slot.Itemstack.Attributes.GetBool("loaded", false), slot.Itemstack.TempAttributes.GetInt("renderVariant"), slot.Itemstack.Attributes.GetInt("renderVariant") );
                
                if (this.Attributes["readyAimSound"].AsString("").Length > 0)
			    {
                    IPlayer byPlayer = null;
                    if (interactingEntity is EntityPlayer) byPlayer = interactingEntity.World.PlayerByUid(((EntityPlayer)interactingEntity).PlayerUID);
                    AssetLocation soundLocation = AssetLocation.Create(this.Attributes["readyAimSound"].AsString(""), this.Code.Domain);
                    interactingEntity.World.PlaySoundAt(soundLocation, interactingEntity, byPlayer, false, 16);
                }
            }
            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack.Collectible.Attributes == null) return;

            float dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);
            if (dmg != 0) dsc.AppendLine(dmg + Lang.Get("piercing-damage"));
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }

    }
}