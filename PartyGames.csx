using System;
using Godot;



private class CustomCommands
{
	private static PartyGamesGm Self; //To access the gamemode instance
	public CustomCommands(PartyGamesGm SelfArg)
	{
		Self = SelfArg;
	}


	public bool Start()
	{
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
}


return new PartyGamesGm();
