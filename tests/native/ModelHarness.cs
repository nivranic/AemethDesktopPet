namespace ZCodePet {
    public static class ModelHarness {
        public static string Run() {
            System.Collections.Generic.List<string> checks=new System.Collections.Generic.List<string>();
            Check(AtlasContract.Cycle(0)==1100,"Idle duration has no sixfold multiplier",checks);
            Check(AtlasContract.Frame(0,279,1)==0 && AtlasContract.Frame(0,280,1)==1,"Exact idle frame boundary",checks);
            Check(AtlasContract.Frame(7,820*100+120,1)==1,"Work still loops after 100 cycles",checks);
            Check(AtlasContract.Frame(0,140,2)==1 && AtlasContract.Frame(0,560,.5)==1,"Supported speed bounds change timing",checks);
            Check(AtlasContract.DisplayCycle(0)==6600,"Displayed idle cycle is sixfold slowed",checks);
            Check(AtlasContract.DisplayFrame(0,1679,1)==0 && AtlasContract.DisplayFrame(0,1680,1)==1,"Slowed idle frame boundary",checks);
            Check(AtlasContract.DisplayFrame(0,839,2)==0 && AtlasContract.DisplayFrame(0,840,2)==1,"Speed applies to the slowed idle loop",checks);
            Check(AtlasContract.NextFrameDelay("idle",0,1)==1680,"Idle scheduler sleeps until the actual next frame",checks);
            Check(AtlasContract.NextFrameDelay("idle",1679,1)==1,"Idle scheduler preserves exact frame boundary",checks);
            Check(AtlasContract.NextFrameDelay("running",0,2)==60,"Scheduler applies speed to active frames",checks);
            Check(AtlasContract.NextFrameDelay("running",2460,1)==1680,"Scheduler switches to slow idle after three loops",checks);
            Check(AtlasContract.NextFrameDelay("jumping",2519,1)==1,"Scheduler preserves final active frame boundary",checks);
            HoverLeaveAnimation leave=new HoverLeaveAnimation();
            Check(!leave.Active(0,1),"Pointer leave effect is absent before interaction",checks);
            leave.Start(1000);
            Check(leave.Active(1000,1) && leave.Frame(1000,1)==0,"Pointer leave starts the sunglasses animation at its first frame",checks);
            Check(leave.Frame(1120,1)==1,"Pointer leave advances real sunglasses frames",checks);
            Check(leave.Active(3459,1) && !leave.Active(3460,1),"Pointer leave lasts exactly three sunglasses cycles",checks);
            Check(leave.Active(2229,2) && !leave.Active(2230,2),"Pointer leave obeys the selected playback speed",checks);
            leave.Start(2000);Check(leave.Frame(2000,1)==0 && leave.Active(4000,1),"A new pointer leave restarts the full interaction",checks);
            leave.Clear();Check(!leave.Active(2001,1),"Pointer reentry or hiding cancels the old effect",checks);
            int seqRow,seqFrame;
            AtlasContract.SequenceFrame("idle",2000,1,out seqRow,out seqFrame);
            Check(seqRow==0,"Idle sequence stays on the idle row",checks);
            AtlasContract.SequenceFrame("jumping",3*840-1,1,out seqRow,out seqFrame);
            Check(seqRow==4,"Transient jumping still plays in its third loop",checks);
            AtlasContract.SequenceFrame("jumping",3*840,1,out seqRow,out seqFrame);
            Check(seqRow==0,"Transient jumping settles after exactly three loops",checks);
            AtlasContract.SequenceFrame("running",3*820-1,1,out seqRow,out seqFrame);
            Check(seqRow==7,"Work animation still plays in its third loop",checks);
            AtlasContract.SequenceFrame("running",3*820+1,1,out seqRow,out seqFrame);
            Check(seqRow==0,"Work animation settles after three loops",checks);
            int[] expectedCounts={6,8,8,4,5,8,6,6,6};int count=0;
            for(int row=0;row<9;row++) {
                Check(AtlasContract.Durations[row].Length==expectedCounts[row],"Animation frame count "+row,checks);int elapsed=0;
                for(int frame=0;frame<expectedCounts[row];frame++) {Check(AtlasContract.Frame(row,elapsed,1)==frame,"Animation boundary "+row+"/"+frame,checks);elapsed+=AtlasContract.Durations[row][frame];count++;}
                Check(AtlasContract.Frame(row,elapsed,1)==0,"Exact loop "+row,checks);
            }
            Check(count==57,"All fifty-seven original animation frames scheduled",checks);
            for(int direction=0;direction<16;direction++) {double a=direction*System.Math.PI/8;Check(AtlasContract.Gaze(System.Math.Sin(a),-System.Math.Cos(a))==direction,"Clockwise look sector "+direction,checks);}
            PetModel model=new PetModel();string a1=new string('a',64),b1=new string('b',64),c1=new string('c',64);
            model.Session(a1,"running",0);model.Session(b1,"failed",0);model.Session(c1,"waiting",0);
            Check(model.Resolve(0)=="waiting","Waiting outranks failure and running",checks);
            model.Session(c1,"ended",1);Check(model.Resolve(1)=="failed","Ended waiting record removed",checks);
            Check(model.Resolve(5000)=="running" && model.Sessions.Count==1,"Five-second failure feedback expires",checks);
            model.Session(b1,"review",6000);Check(model.Resolve(6000)=="review","Review outranks running",checks);
            Check(model.Resolve(36000)=="running" && model.Sessions.Count==1,"Thirty-second review record removed",checks);
            model.TemporaryState="waving";model.TemporaryUntil=38000;
            Check(model.Resolve(37999)=="waving" && model.Resolve(38000)=="running","Temporary animation expires to aggregate work",checks);
            model.IdleQueueEnabled=false;
            Check(model.Resolve(1800000)=="idle" && model.Sessions.Count==0 && model.ExpiredSessions==1,"Thirty-minute unknown work display reclaimed and counted",checks);
            PetModel bounded=new PetModel();for(int i=0;i<129;i++) bounded.Session(i.ToString("x64"),"idle",i);
            Check(bounded.Sessions.Count==128 && !bounded.Sessions.ContainsKey(new string('0',64)),"129th session evicts the oldest idle record",checks);
            bounded.Session(new string('d',64),"running",200);Check(bounded.Sessions.Count==128,"New active session can replace an idle record",checks);
            PetModel active=new PetModel();for(int i=0;i<128;i++) active.Session(i.ToString("x64"),"waiting",0);
            bool rejected=false;try {active.Session(new string('e',64),"running",1);}catch(System.ArgumentException) {rejected=true;}
            Check(rejected && active.Sessions.Count==128,"Active capacity is bounded without dropping live records",checks);
            active.IdleQueueEnabled=false;
            Check(active.Resolve(1800000)=="idle" && active.ExpiredSessions==128,"All missing-event leases expire without completion claims",checks);
            PetModel queue=new PetModel();
            Check(queue.Resolve(19999)=="idle","Idle queue waits out its delay",checks);
            Check(queue.Resolve(20000)=="failed","Idle queue starts with the question-mark animation",checks);
            Check(queue.Resolve(20000+3*1220-1)=="failed" && queue.Resolve(20000+3*1220)=="idle","Queued animation plays three loops then settles",checks);
            Check(queue.Resolve(20000+3*1220+39999)=="idle" && queue.Resolve(20000+3*1220+40000)=="review","Gap holds then queue advances to review",checks);
            queue.Session(a1,"running",20000+3*1220+40000+3090);
            Check(queue.Resolve(20000+3*1220+40000+3091)=="running","Active task suspends the idle queue",checks);
            queue.Session(a1,"ended",20000+3*1220+40000+3090+5000);
            long resume=20000+3*1220+40000+3090+5000;
            Check(queue.Resolve(resume+15000)=="idle" && queue.Resolve(resume+15001)=="failed","Queue resumes after activity ends",checks);
            rejected=false;try {model.Session("bad","running",0);}catch(System.ArgumentException) {rejected=true;}Check(rejected,"Invalid session identity rejected",checks);
            rejected=false;try {Protocol.Number("1",.5,2,"speed");}catch(System.ArgumentException) {rejected=true;}Check(rejected,"Numeric strings rejected",checks);
            rejected=false;try {Protocol.Number(3,.5,2,"speed");}catch(System.ArgumentException) {rejected=true;}Check(rejected,"Out of range speed rejected",checks);
            rejected=false;try {Protocol.Boolean("false","followPointer");}catch(System.ArgumentException) {rejected=true;}Check(rejected,"Boolean strings rejected",checks);
            Settings settings=new Settings();Settings.Apply(settings,new System.Collections.Generic.Dictionary<string,object>{{"speed",1.5},{"scale",.75},{"followPointer",false},{"reducedMotion",true}},false);
            Check(settings.Speed==1.5 && settings.Scale==.75 && !settings.FollowPointer && settings.ReducedMotion,"Whitelisted settings apply",checks);
            rejected=false;try {Settings.Apply(settings,new System.Collections.Generic.Dictionary<string,object>{{"atlas","other.png"}},false);}catch(System.ArgumentException) {rejected=true;}Check(rejected,"Settings cannot alter file paths",checks);
            Check(!new Settings().FollowPointer,"Pointer follow defaults off so task animations are never masked",checks);
            System.Collections.Generic.Dictionary<string,object> legacy=new System.Collections.Generic.Dictionary<string,object>{{"speed",1.5},{"scale",1},{"followPointer",true},{"reducedMotion",false}};
            Settings migrated=Settings.FromDocument(new Settings(),legacy);
            Check(!migrated.FollowPointer && migrated.Speed==1.5,"Unversioned document drops the old defaulted followPointer but keeps other fields",checks);
            System.Collections.Generic.Dictionary<string,object> versioned=new System.Collections.Generic.Dictionary<string,object>{{"speed",1.5},{"scale",1},{"followPointer",true},{"reducedMotion",false},{"settingsVersion",2}};
            Settings kept=Settings.FromDocument(new Settings(),versioned);
            Check(kept.FollowPointer && kept.Speed==1.5 && !versioned.ContainsKey("settingsVersion"),"Versioned document preserves an explicit followPointer choice",checks);
            // Json serialization below is deliberately done by the PowerShell caller; production parsing is Framework-only.
            return string.Join("\n",checks.ToArray());
        }
        private static void Check(bool pass,string text,System.Collections.Generic.List<string> checks) {if(!pass) throw new System.InvalidOperationException(text);checks.Add(text);}
    }
}
