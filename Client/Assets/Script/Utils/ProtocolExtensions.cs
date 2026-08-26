using Protocol;

// PacketGenerator가 만드는 Protocol 타입들은 평범한 C# 클래스라
// protobuf가 제공하던 MergeFrom/CopyFrom이 없다. 호출부를 그대로 두기 위한 대체 확장.
public static class ProtocolExtensions
{
	public static void MergeFrom(this StatInfo dest, StatInfo src)
	{
		if (dest == null || src == null)
			return;

		dest.Level = src.Level;
		dest.Hp = src.Hp;
		dest.MaxHp = src.MaxHp;
		dest.Attack = src.Attack;
		dest.Speed = src.Speed;
		dest.TotalExp = src.TotalExp;
	}
}
