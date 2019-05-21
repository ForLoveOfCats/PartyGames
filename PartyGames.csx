using System;
using System.Linq;
using System.Collections.Generic;
using Godot;


const float MODE_START_DELAY = 5f; //In seconds
const int STRUCTURES_REMOVE_RATE = 5; //Structures per second

enum MODE {NONE, LAVA};
static MODE CurrentMode = MODE.NONE;
static bool Playing = false; //Is the *local* player currently playing

static Dictionary<int, int> Scores = new Dictionary<int, int>();
static List<int> PlayingPeers = new List<int>();

static float StartTimer = 0f; //In seconds
static float TimeToRemoveStructure = 1/STRUCTURES_REMOVE_RATE;

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

				if(Net.Work.IsNetworkServer())
				{
					TimeToRemoveStructure -= Delta;
					if(TimeToRemoveStructure <= 0f)
					{
						TimeToRemoveStructure = 1/STRUCTURES_REMOVE_RATE;
						List<Structure> Structures = World.Chunks.ElementAt(Rand.Next(0, World.Chunks.Count)).Value.Structures;
						Structures[Rand.Next(0, Structures.Count)].NetRemove();
					}
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

			if(StartTimer >= MODE_START_DELAY)
			{
				Playing = true;
				MessageLabel.Hide();

				PlayingPeers.Clear();
				foreach(int Peer in Net.PeerList)
				{
					PlayingPeers.Add(Peer);
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
		StartLava();
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
	public void ReportLoss(int Id) //Run on every other player upon loss
	{
		PlayingPeers.Remove(Id);

		if(CurrentMode == MODE.LAVA && Playing && PlayingPeers.Count <= 1)
		{
			Win();
			PlayingPeers.Clear();
		}
	}


	public void Lose()
	{
		Game.PossessedPlayer.SetFreeze(true);
		Game.PossessedPlayer.MovementReset();

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
}


return new PartyGamesGm();
