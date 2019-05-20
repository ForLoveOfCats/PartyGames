using System;
using Godot;


const float MODE_START_DELAY = 5f; //In seconds

enum MODE {NONE, LAVA};
static MODE CurrentMode = MODE.NONE;
static bool Playing = false; //Is the *local* player currently playing

static float StartTimer = 0f; //In seconds



private class CustomCommands
{
	private static PartyGamesGm Self; //To access the gamemode instance
	public CustomCommands(PartyGamesGm SelfArg)
	{
		Self = SelfArg;
	}


	public bool Start()
	{
		Self.StartLava();
		return true;
	}

	public bool Reset()
	{
		CurrentMode = MODE.NONE;
		Playing = false;

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
		}
		else if(CurrentMode != MODE.NONE)
		{
			StartTimer += Delta;

			if(StartTimer >= MODE_START_DELAY)
			{
				Playing = true;
			}
		}
	}


	[Remote]
	public void StartLava()
	{
		CurrentMode = MODE.LAVA;

		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(this, nameof(StartLava));
	}


	public void Lose()
	{
		Game.PossessedPlayer.SetFreeze(true);
		Game.PossessedPlayer.MovementReset();
	}


	public override void OnPlayerConnect(int Id)
	{
		if(Net.Work.IsNetworkServer())
			Scripting.Self.RpcId(Id, nameof(Scripting.RequestGmLoad), OwnName); //Load same gamemode on newly connected client
	}


	public override void OnUnload()
	{
		if(Net.Work.IsNetworkServer())
			Net.SteelRpc(Scripting.Self, nameof(Scripting.RequestGmUnload)); //Unload gamemode on all clients

		API.Gm = new API.EmptyCustomCommands();
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
