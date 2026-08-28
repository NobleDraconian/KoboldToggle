using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using MonoMod.Utils;
using ReLogic.Content;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace KoboldToggle
{
	public class KoboldToggle : Mod
	{
		internal enum MessageType : byte
		{
			SyncKoboldMode
		}

		internal static Dictionary<int, Asset<Texture2D>>[] Kobold;

		internal static Dictionary<int, Asset<Texture2D>> KoboldHair;

		internal static Asset<Texture2D> KoboldIcon;

		internal static Asset<Texture2D> KoboldIconHighlight;

		internal static FieldInfo MapHeightReflection;

		// Loads all the assets, reflections and edits
		public override void Load()
		{
			MapHeightReflection = typeof(Main).GetField("mH", BindingFlags.NonPublic | BindingFlags.Static);

			if (!Main.dedServ)
			{
				Kobold = new Dictionary<int, Asset<Texture2D>>[8];

				int[] koboldVari0 = { 0, 1, 2, 5, 7, 9, 10, 11, 12, 15 }; // variation 0
				int[] koboldVari1 = { 11, 12 }; // varitation 1, 2, 3, 5, 6, 7
				int[] koboldVari2 = { 10, 11, 12 }; // variation 4

				for (int i = 0; i < 8; i++)
				{
					Kobold[i] = [];

					int[] variationsToGet = i == 0 ? koboldVari0 : (i == 4 ? koboldVari2 : koboldVari1);

					foreach (int j in variationsToGet)
					{
						Kobold[i].Add(
							j,
							ModContent.Request<Texture2D>(Name + "/Assets/KoboldSkin/Kobold_" + i + "_" + j)
							);
					}
				}

				KoboldHair = [];

				// List of all the hair IDs that will be replaced
				int[] koboldHorns = { 1, 5, 8, 13, 16, 18, 20, 22, 25, 32, 33, 35, 37, 40, 42, 43, 50, 52, 54, 55 };

				foreach (int i in koboldHorns)
					KoboldHair.Add(i - 1, ModContent.Request<Texture2D>(Name + "/Assets/KoboldHorns/Kobold_Horns_" + i));

				KoboldIcon = ModContent.Request<Texture2D>(Name + "/Assets/Icons/KoboldIcon");
				KoboldIconHighlight = ModContent.Request<Texture2D>(Name + "/Assets/Icons/KoboldIcon_Highlight");

				// Instead of sitting here and writing out each IL edit manually, we apply the same edit across the whole PlayerDrawLayers class (where eligable)

				int[] playerLayersToEdit = [12, 13, 15, 17, 21, 28];
				int[] playerLayersToEditHair = [01, 21];

				// Uses "GetMethods" to obtain all method assembly and then sifts through their names to check if they are valid to recieve any specific IL edits
				MethodInfo[] DrawingMethods = typeof(PlayerDrawLayers).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
				for (int i = 0; i < DrawingMethods.Length; i++)
				{
					if (DrawingMethods[i].Name is nameof(ToString) or nameof(GetType) or nameof(GetHashCode) or nameof(Equals)) // Default object methods added to any class. These are not to be messed with
					{
						continue;
					}
					// Apply skin edits
					foreach (int j in playerLayersToEdit)
					{
						if (DrawingMethods[i].Name.Contains(""+j))
						{
							MonoModHooks.Modify(
								DrawingMethods[i],
								EditPlayerAssets
							);
							break;
						}
					}
					// Apply hair edits
					foreach (int j in playerLayersToEditHair)
					{
						if (DrawingMethods[i].Name.Contains("" + j))
						{
							MonoModHooks.Modify(
								DrawingMethods[i],
								EditPlayerHairAssets
							);
							break;
						}
					}
				}
				IL_PlayerDrawLayers.DrawPlayer_21_Head_TheFace += EditPlayerEyelids;
			}
			// Due to this being rendering, might be unsafe to apply outside of !Main.dedServ. Although in testing, no effects have been noticed
			On_Main.DrawInventory += AddKoboldToggle;
			IL_Main.DrawHairWindow += EditStylistMenu;
		}

		// Unloads all the saved content loaded from this mod
		public override void Unload()
		{
			if (!Main.dedServ)
			{
				Kobold = null;
				KoboldHair = null;
				KoboldIcon = null;
				KoboldIconHighlight = null;
			}
			MapHeightReflection = null;
		}

		private void AddKoboldToggle(On_Main.orig_DrawInventory orig, Main self)
		{
			// Not needed as an IL edit due to the PVP icons being the first thing rendered on the inventory
			DrawKoboldToggle(self);
			orig.Invoke(self);
		}

		// Renders the icon used for switching to and from kobold mode
		public static void DrawKoboldToggle(Main self)
		{
			Main.inventoryScale = 0.6f;
			Player player = Main.LocalPlayer;
			KoboldPlayer kPlayer = player.GetModPlayer<KoboldPlayer>();

			// Based off of the inventory pvp icon positioning and scale, to make the icon better match with other icons in vanilla
			int iconScale = (int)(52f * Main.inventoryScale);
			int iconX = (707 - iconScale * 4 + Main.screenWidth - 800) + 10;
			int iconY = (114 + (int)MapHeightReflection.GetValue(null) + iconScale * 2 + iconScale / 2 - 12) + 60;

			if (Main.EquipPage == 2)
				iconX += iconScale + iconScale / 2;

			// Moves the icon across to the left when the pvp menu is drawn
			if (Main.ShouldPVPDraw)
				iconX -= 40;

			Rectangle frame = KoboldIcon.Frame(verticalFrames: 2);
			frame.Location = new Point(iconX, iconY + 1 / 2 * 20);

			// Selection, highlight and player packet syncing 
			bool highlight = false;
			if (frame.Contains(Main.MouseScreen.ToPoint()) && !PlayerInput.IgnoreMouseInterface)
			{
				player.mouseInterface = true;
				highlight = true;
				if (Main.mouseLeft && Main.mouseLeftRelease)
				{
					SoundEngine.PlaySound(SoundID.MenuTick);
					kPlayer.IsKobold = !kPlayer.IsKobold;
					kPlayer.SyncPlayer(toWho: -1, fromWho: Main.myPlayer, newPlayer: false);
				}
			}
			// Render the highlight
			if (highlight)
				Main.spriteBatch.Draw(KoboldIconHighlight.Value, frame.Location.ToVector2() + new Vector2(-2f), Color.White);
			// Render the main Icon
			Main.spriteBatch.Draw(KoboldIcon.Value, frame.Location.ToVector2(), new Rectangle(0, kPlayer.IsKobold ? KoboldIcon.Height() / 2 : 0, KoboldIcon.Width(), KoboldIcon.Height() / 2), Color.White);
		}

		private static Asset<Texture2D> GetKoboldSkinLayer(ref PlayerDrawSet drawInfo, int skinID)
		{
			int skinVar = drawInfo.skinVar;
			Player player = drawInfo.drawPlayer;
			if (player.GetModPlayer<KoboldPlayer>().IsKobold && Kobold.Length > skinVar)
			{
				if (Kobold[skinVar].TryGetValue(skinID, out Asset<Texture2D> texture))
					return texture;
				else if (Kobold[0].TryGetValue(skinID, out Asset<Texture2D> texture2))
					return texture2;
			}
			return null;
		}

		private void EditPlayerAssets(ILContext il)
		{
			ILCursor c = new(il);
			
			// Check for any use of "TextureAssets.Player[drawInfo.skinVar, num].Value", and then override it with our own texture based under the condition in the delegate
			while (true)
			{
				int drawInfo_varNum = -1; // Save the arguement ID for DrawInfo to be used later
				int skinLayerID = -1; // Save the skin ID index that was attempted to be accessed

				// Try to see if there are any more uses of "TextureAssets.Player[drawInfo.skinVar, num].Value" within the method.
				// Otherwise, break out of the loop/stop IL editing
				if (!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.Players)),
					i => i.MatchLdarg(out drawInfo_varNum),
					i => i.MatchLdfld<PlayerDrawSet>(nameof(PlayerDrawSet.skinVar)),
					i => i.MatchLdcI4(out skinLayerID),
					i => i.MatchCall<Asset<Texture2D>[,]>("Get"),
					i => i.MatchCallvirt<Asset<Texture2D>>("get_Value")))
				{
					break;
				}

				// Inject a delegate that changes the player's skins' texture if they are a kobold or not
				c.EmitLdarg(drawInfo_varNum);
				c.EmitLdcI4(skinLayerID);
				c.EmitDelegate((Texture2D curPlayerSkin, ref PlayerDrawSet drawInfo, int skinID) =>
				{
					Asset<Texture2D> texture = GetKoboldSkinLayer(ref drawInfo, skinID);
					if (texture == null)
						return curPlayerSkin;
					else
						return texture.Value;
				});
			}
		}

		private void EditPlayerEyelids(ILContext il)
		{
			ILCursor c = new(il);

			// Clone of the EditPlayerAssets
			// Instead of looking and editng around "TextureAssets.Player[drawInfo.skinVar, num].Value", it edits "TextureAssets.Player[drawInfo.skinVar, num]"
			// Note, notice its editing a Asset<Texture2D> instead of a Texture2D
			while (true)
			{
				int drawInfo_varNum = -1;
				int skinLayerID = -1;

				if (!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.Players)),
					i => i.MatchLdarg(out drawInfo_varNum),
					i => i.MatchLdfld<PlayerDrawSet>(nameof(PlayerDrawSet.skinVar)),
					i => i.MatchLdcI4(out skinLayerID),
					i => i.MatchCall<Asset<Texture2D>[,]>("Get")))
				{
					break;
				}

				c.EmitLdarg(drawInfo_varNum);
				c.EmitLdcI4(skinLayerID);
				c.EmitDelegate((Asset<Texture2D> playerEyelid, ref PlayerDrawSet drawInfo, int skinID) =>
				{
					return GetKoboldSkinLayer(ref drawInfo, skinID) ?? playerEyelid;
				});
			}
		}

		private void EditPlayerHairAssets(ILContext il)
		{
			ILCursor c = new(il);

			// This is an edit very similar to the edit above
			// The only change is that it checks for both "TextureAssets.PlayerHair[drawInfo.drawPlayer.hair].Value" and "TextureAssets.PlayerHairAlt[drawInfo.drawPlayer.hair].Value"
			// Skin index also isnt saved here as there isn't any index to save and use
			while (true)
			{
				int drawInfo_varNum = -1;
				if (!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.PlayerHair)),
					i => i.MatchLdarg(out drawInfo_varNum),
					i => i.MatchLdfld<PlayerDrawSet>(nameof(PlayerDrawSet.drawPlayer)),
					i => i.MatchLdfld<Player>(nameof(Player.hair)),
					i => i.MatchLdelemRef(),
					i => i.MatchCallvirt<Asset<Texture2D>>("get_Value")) 
					&& 
					!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.PlayerHairAlt)),
					i => i.MatchLdarg(out drawInfo_varNum),
					i => i.MatchLdfld<PlayerDrawSet>(nameof(PlayerDrawSet.drawPlayer)),
					i => i.MatchLdfld<Player>(nameof(Player.hair)),
					i => i.MatchLdelemRef(),
					i => i.MatchCallvirt<Asset<Texture2D>>("get_Value")))
				{
					break;
				}

				c.EmitLdarg(drawInfo_varNum);
				c.EmitDelegate((Texture2D curPlayerSkin, ref PlayerDrawSet drawInfo) =>
				{
					Player player = drawInfo.drawPlayer;
					int hair = player.hair;
					if (player.GetModPlayer<KoboldPlayer>().IsKobold)
					{
						if (KoboldHair.TryGetValue(hair, out Asset<Texture2D> texture))
						{
							return texture.Value;
						}
					}
					return curPlayerSkin;
				});
			}
		}

		private void EditStylistMenu(ILContext il)
		{
			ILCursor c = new(il);

			// Very similar to the edits above
			// edits the stylist menu to replace all cases of "TextureAssets.PlayerHair[num].Value" and "TextureAssets.Player[num, num2].Value"
			while (true)
			{
				int hairStyle_varNum = -1;
				if (!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.PlayerHair)),
					i => i.MatchLdloc(out hairStyle_varNum),
					i => i.MatchLdelemRef(),
					i => i.MatchCallvirt<Asset<Texture2D>>("get_Value")))
				{
					break;
				}

				c.EmitLdloc(hairStyle_varNum);
				c.EmitDelegate((Texture2D curPlayerSkin, int hairStyle) =>
				{
					if (Main.LocalPlayer.GetModPlayer<KoboldPlayer>().IsKobold)
					{
						if (KoboldHair.TryGetValue(hairStyle, out Asset<Texture2D> texture))
						{
							return texture.Value;
						}
					}
					return curPlayerSkin;
				});
			}

			c.Index = 0; // Index 0 reffers to the first instruction in the IL (effectively allowing loops to itterate over the whole IL again)
			while (true)
			{
				int playerStyle_varNum = -1; 
				int skinLayerID = -1;

				if (!c.TryGotoNext(MoveType.After,
					i => i.MatchLdsfld("Terraria.GameContent.TextureAssets", nameof(TextureAssets.Players)),
					i => i.MatchLdloc(out playerStyle_varNum),
					i => i.MatchLdcI4(out skinLayerID),
					i => i.MatchCall<Asset<Texture2D>[,]>("Get"),
					i => i.MatchCallvirt<Asset<Texture2D>>("get_Value")))
				{
					break;
				}

				c.EmitLdloc(playerStyle_varNum);
				c.EmitLdcI4(skinLayerID);
				c.EmitDelegate((Texture2D curPlayerSkin, int player, int skinID) =>
				{
					if (Main.LocalPlayer.GetModPlayer<KoboldPlayer>().IsKobold && Kobold.Length > player)
					{
						if (Kobold[player].TryGetValue(skinID, out Asset<Texture2D> texture))
							return texture.Value;
						else if (Kobold[0].TryGetValue(skinID, out Asset<Texture2D> texture2))
							return texture2.Value;
					}
					return curPlayerSkin;
				});
			}
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			MessageType msgType = (MessageType)reader.ReadByte();
			switch (msgType)
			{
				case MessageType.SyncKoboldMode:
					byte playerNumber = reader.ReadByte();
					bool koboldMode = reader.ReadBoolean();

					Player player = Main.player[playerNumber];
					KoboldPlayer koboldPlayer = player.GetModPlayer<KoboldPlayer>();
					koboldPlayer.IsKobold = koboldMode;

					if (Main.netMode == NetmodeID.Server)
						koboldPlayer.SyncPlayer(-1, whoAmI, false);

					break;
			}
		}
	}

	// Saves the player's choice, as well as writes the packs to send to other clients/the server
	public class KoboldPlayer : ModPlayer
	{
		public bool IsKobold = false;

		public override void SaveData(TagCompound tag)
		{
			tag["IsKobold"] = IsKobold;
		}

		public override void LoadData(TagCompound tag)
		{
			IsKobold = tag.GetBool("IsKobold");
		}

		public override void SyncPlayer(int toWho, int fromWho, bool newPlayer)
		{
			if (Main.netMode == NetmodeID.SinglePlayer)
				return;

			ModPacket packet = Mod.GetPacket();
			packet.Write((byte)KoboldToggle.MessageType.SyncKoboldMode);
			packet.Write((byte)Player.whoAmI);
			packet.Write(!IsKobold);
			packet.Send(toWho, fromWho);
		}
	}
}
