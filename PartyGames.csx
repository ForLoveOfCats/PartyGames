using System;
using System.Linq;
using System.Collections.Generic;
using Godot;


const float MODE_START_DELAY = 5f; //In seconds
const int TILE_REMOVE_RATE = 1; //Tiles per second

enum MODE {NONE, LAVA, SPLEEF, FIND};
static MODE CurrentMode = MODE.NONE;
static bool Playing = false; //Is the *local* player currently playing

static Dictionary<int, int> Scores = new Dictionary<int, int>();
static List<int> PlayingPeers = new List<int>();

static float StartTimer = 0f; //In seconds
static float TimeToRemoveTile = 1/TILE_REMOVE_RATE;

static Random Rand = new Random();
static Node MiniHud;
static Label MessageLabel;
static VBoxContainer ScoreContainer;

static PackedScene ScoreLabelScene;



private class CustomCommands
{
	private static PartyGamesGm Self; //To access the gamemode instance
	public CustomCommands(PartyGamesGm SelfArg)
	{
		Self = SelfArg;
	}


	public bool Start()
	{
		if(Net.Work.IsNetworkServer())
		{
			Self.StartNext();
			return true;
		}

		return false;
	}

	public bool Reset()
	{
		if(CurrentMode == MODE.LAVA)
			API.ReloadSave();

		StartTimer = 0f;
		CurrentMode = MODE.NONE;
		Playing = false;
		MessageLabel.Hide();

		Game.PossessedPlayer.SetFreeze(false);
		Game.PossessedPlayer.Inventory = new Items.Instance[10];
		Game.PossessedPlayer.HUDInstance.HotbarUpdate();

		return true;
	}
}



public class PartyGamesGm : Gamemode
{
	public override void _Ready()
	{
		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(Scripting.Self, nameof(Scripting.RequestGmLoad), OwnName); //Load same gamemode on all connected clients

		API.Gm = new CustomCommands(this);

		MiniHud = GD.Load<PackedScene>($"{LoadPath}/MiniHud.tscn").Instance() as Node;
		MessageLabel = MiniHud.GetNode<Label>("VBoxContainer/CenterContainer/MessageLabel");
		ScoreContainer = MiniHud.GetNode<VBoxContainer>("HBoxContainer/VBoxContainer/ScoreContainer");
		Game.PossessedPlayer.HUDInstance.GetNode("CLayer").AddChild(MiniHud);

		ScoreLabelScene = GD.Load<PackedScene>("res://UI/ItemCountLabel.tscn");

		if(Net.Work.IsNetworkServer())
		{
			foreach(int Id in Net.PeerList)
			{
				Scores[Id] = 0;
			}
			UpdateHudScores();
		}
		else
			RpcId(Net.ServerId, nameof(SyncAllScores), Net.Work.GetNetworkUniqueId());

		API.Gm.Reset();
	}


	public override void _Process(float Delta)
	{
		if(Playing)
		{
			if(CurrentMode == MODE.LAVA)
			{
				if(Game.PossessedPlayer.IsOnFloor())
				{
					Lose();
				}
			}
			else if(CurrentMode == MODE.SPLEEF)
			{
				if(Game.PossessedPlayer.Translation.y <= 5*World.PlatformSize)
				{
					Console.Log("Mode is spleef");
					Lose();
				}
			}
		}
		else if(CurrentMode != MODE.NONE)
		{
			StartTimer += Delta;

			if(CurrentMode == MODE.LAVA)
			{
				MessageLabel.Text = $"The floor is lava in {(int)(MODE_START_DELAY-StartTimer)} seconds!";
			}
			else if(CurrentMode == MODE.SPLEEF)
			{
				MessageLabel.Text = $"Spleef begins in {(int)(MODE_START_DELAY-StartTimer)} seconds!";
			}
			else if(CurrentMode == MODE.FIND)
			{
				MessageLabel.Text = $"Find the item starts in {(int)(MODE_START_DELAY-StartTimer)} seconds!";
			}

			if(StartTimer >= MODE_START_DELAY)
			{
				Playing = true;
				MessageLabel.Hide();

				PlayingPeers.Clear();
				foreach(int Peer in Net.PeerList)
				{
					PlayingPeers.Add(Peer);
				}

				if(CurrentMode == MODE.SPLEEF)
				{
					Game.PossessedPlayer.SetFreeze(false);
				}

				if(CurrentMode == MODE.FIND && Net.Work.IsNetworkServer())
				{
					bool Placed = false;
					while(!Placed)
					{
						List<Tile> Tiles = World.Chunks.ElementAt(Rand.Next(0, World.Chunks.Count)).Value.Tiles;
						if(Tiles.Count <= 0)
							continue;
						Tile Item = Tiles[Rand.Next(0, Tiles.Count)];
						if(Item.Type == Items.ID.PLATFORM)
						{
							Vector3 Trans = Item.Translation;
							Trans.y += 1;
							World.Self.DropItem(Items.ID.PLATFORM, Trans, new Vector3());
							Placed = true;
							Console.Log("Placed item");
						}
					}
				}
			}
		}
	}


	public void UpdateHudScores()
	{
		foreach(Node Child in ScoreContainer.GetChildren())
		{
			Child.QueueFree();
		}

		foreach(KeyValuePair<int, int> Pair in Scores)
		{
			Label ScoreLabel = ScoreLabelScene.Instance() as Label;
			ScoreLabel.SetAlign(Label.AlignEnum.Right);
			ScoreLabel.Text = $"{Net.Nicknames[Pair.Key]}: {Pair.Value}";
			ScoreContainer.AddChild(ScoreLabel);
		}
	}


	[Remote]
	public void ClearScores()
	{
		Scores.Clear();
	}


	[Remote]
	public void SyncScore(int Id, int Score) //Yes we are syncing scores one at a time. Why? Because I am lazy
	{
		Scores[Id] = Score;
		UpdateHudScores();
	}


	[Remote]
	public void SyncAllScores(int Reciever)
	{
		RpcId(Reciever, nameof(ClearScores));
		foreach(KeyValuePair<int, int> Pair in Scores)
		{
			RpcId(Reciever, nameof(SyncScore), Pair.Key, Pair.Value);
		}
	}


	public void StartNext()
	{
		End();

		int Num = Rand.Next(0,3);
		switch(Num)
		{
			case 0:
				StartLava();
				break;

			case 1:
				StartSpleef();
				break;

			case 2:
				StartFind();
				break;
		}
	}


	[Remote]
	public void End()
	{
		Game.PossessedPlayer.SetFreeze(false);
		Game.PossessedPlayer.MovementReset();
		Game.PossessedPlayer.Inventory = new Items.Instance[10];
		Game.PossessedPlayer.HUDInstance.HotbarUpdate();
	}


	[Remote]
	public void StartLava()
	{
		StartTimer = 0f;
		CurrentMode = MODE.LAVA;

		MessageLabel.Show();

		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(this, nameof(StartLava));
	}


	[Remote]
	public void StartSpleef()
	{
		StartTimer = 0f;
		CurrentMode = MODE.SPLEEF;

		MessageLabel.Show();

		World.Clear();
		if(Net.Work.IsNetworkServer())
		{
			Net.SteelRpc(this, nameof(StartSpleef));

			World.DefaultPlatforms();

			int Size = 20;
			for(int X = (int)(-Size/2); X <= (int)(Size/2); X++)
			{
				for(int Z = (int)(-Size/2); Z <= (int)(Size/2); Z++)
				{
					World.Place(Items.ID.PLATFORM, new Vector3(X*World.PlatformSize, 10*World.PlatformSize, Z*World.PlatformSize), new Vector3(), 1);
				}
			}
		}

		Game.PossessedPlayer.Translation = new Vector3(0,12*World.PlatformSize,0);
		Game.PossessedPlayer.SetFreeze(true);
	}


	[Remote]
	public void StartFind()
	{
		StartTimer = 0f;
		CurrentMode = MODE.FIND;

		MessageLabel.Show();

		Game.PossessedPlayer.MovementReset();

		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(this, nameof(StartFind));
	}


	[Remote]
	public void ReportLoss(int Id) //Run on every other player upon loss
	{
		PlayingPeers.Remove(Id);

		if( (CurrentMode == MODE.LAVA || CurrentMode == MODE.SPLEEF) && Playing && PlayingPeers.Count <= 1)
		{
			Win();
			PlayingPeers.Clear();
		}
	}


	[Remote]
	public void Lose()
	{
		Game.PossessedPlayer.SetFreeze(true);
		Game.PossessedPlayer.MovementReset();
		Playing = false;
		CurrentMode = MODE.NONE;

		MessageLabel.Show();
		MessageLabel.Text = "You lost!";

		Net.SteelRpc(this, nameof(ReportLoss), Net.Work.GetNetworkUniqueId());
	}


	[Remote]
	public void ReportWin(int Id) //Run on the server upon win
	{
		if(!Net.Work.IsNetworkServer())
			return;

		Scores[Id] += 1;
		UpdateHudScores();

		foreach(int Peer in Net.PeerList)
		{
			if(Peer == Net.Work.GetNetworkUniqueId())
				continue;

			SyncAllScores(Peer);
		}

		End();
		Net.SteelRpc(this, nameof(End));
	}


	[Remote]
	public void Win()
	{
		MessageLabel.Show();
		MessageLabel.Text = "You won!";
		Playing = false;
		CurrentMode = MODE.NONE;

		if(Net.Work.IsNetworkServer())
			ReportWin(Net.ServerId);
		else
			RpcId(Net.ServerId, nameof(ReportWin), Net.Work.GetNetworkUniqueId());
	}


	public override void OnPlayerConnect(int Id)
	{
		if(Net.Work.IsNetworkServer())
		{
			Scripting.Self.RpcId(Id, nameof(Scripting.RequestGmLoad), OwnName); //Load same gamemode on newly connected client
			Scores[Id] = 0;
			UpdateHudScores();

			foreach(int Peer in Net.PeerList)
			{
				if(Peer != Id && Peer != Net.ServerId)
					SyncAllScores(Peer);
			}
		}
	}


	public override void OnPlayerDisconnect(int Id)
	{
		if(Net.Work.IsNetworkServer())
		{
			Scores.Remove(Id);
			UpdateHudScores();

			foreach(int Peer in Net.PeerList)
			{
				if(Peer != Id && Peer != Net.ServerId)
					SyncAllScores(Peer);
			}
		}
	}


	public override void OnUnload()
	{
		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(Scripting.Self, nameof(Scripting.RequestGmUnload)); //Unload gamemode on all clients

		API.Gm = new API.EmptyCustomCommands();
		MiniHud.QueueFree();
	}


	public override bool ShouldPlayerMove(Vector3 Position)
	{
		if(Playing)
		{
			if(CurrentMode == MODE.LAVA)
			{
				if(Position.y < -(World.PlatformSize*10))
				{
					Lose();
					return false;
				}
			}
		}

		return true;
	}


	public override void OnPlayerCollide(KinematicCollision Collision)
	{
		if(Playing && CurrentMode == MODE.LAVA)
		{
			if(Collision.Collider is Tile Item)
			{
				Item.NetRemove();
			}
		}
	}


	public override bool ShouldPickupItem(Items.ID Id)
	{
		if(CurrentMode == MODE.FIND)
		{
			Win();
			CallDeferred(nameof(End));
			Net.SteelRpc(this, nameof(Lose));
			Net.SteelRpc(this, nameof(End));
		}

		return true;
	}


	public override bool ShouldPlaceTile(Items.ID BranchId, Vector3 Position, Vector3 Rotation)
	{
		return false;
	}


	public override bool ShouldRemoveTile(Items.ID BranchId, Vector3 Position, Vector3 Rotation, int OwnerId)
	{
		if(CurrentMode == MODE.SPLEEF)
			return true;

		return false;
	}
}


return new PartyGamesGm();
