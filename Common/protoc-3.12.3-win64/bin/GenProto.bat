protoc.exe -I=./ --csharp_out=./ ./Protocol.proto 
IF ERRORLEVEL 1 PAUSE

START ../../../MMO_Server/PacketGenerator/bin/PacketGenerator.exe ./Protocol.proto
XCOPY /Y Protocol.cs "../../../Client/Assets/Scripts/Packet"
XCOPY /Y Protocol.cs "../../../MMO_Server/DummyClient/Packet"
XCOPY /Y Protocol.cs "../../../MMO_Server/Server/Packet"
XCOPY /Y ClientPacketManager.cs "../../../Client/Assets/Scripts/Packet"
XCOPY /Y ClientPacketManager.cs "../../../MMO_Server/DummyClient/Packet"
XCOPY /Y ServerPacketManager.cs "../../../MMO_Server/Server/Packet"