using SaabDidTool.Models;
static void Check(bool b, string name) { if (!b) throw new Exception(name); Console.WriteLine("PASS " + name); }
byte[] uec = {0x8B,0xF1,0x6B,0x6B};
byte[] rec = {0x8B,0xF8,0xFC,0x38,0x38,0x26};
byte[] uMasks = {0x80,0x40,0x20,0x10,0x01};
for(int i=0;i<5;i++) { byte[] c=(byte[])uec.Clone(); c[1]=(byte)(c[1]&~uMasks[i]); Check(c[0]==0x8B && c[1]==(byte)(0xF1&~uMasks[i]) && c[2]==0x6B && c[3]==0x6B, "UEC OFF "+i); c[1]|=uMasks[i]; Check(c.SequenceEqual(uec), "UEC ON "+i); }
byte[] rMasks={0x80,0x40,0x20,0x10,0x08};
for(int i=0;i<6;i++) { byte[] c=(byte[])rec.Clone(); int index=i==5?2:1; byte mask=i==5?(byte)0x08:rMasks[i]; c[index]=(byte)(c[index]&~mask); Check(c[0]==0x8B && c[index]==(byte)(rec[index]&~mask) && c[3]==0x38 && c[4]==0x38 && c[5]==0x26, "REC OFF "+i); c[index]|=mask; Check(c.SequenceEqual(rec), "REC ON "+i); }
