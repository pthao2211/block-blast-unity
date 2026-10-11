using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BlockBlast
{
    public sealed class BlastGame : MonoBehaviour
    {
        public BlastModel Model { get; private set; }
        public string ScreenName { get; private set; }
        const string SaveKey = "BlockBlast.Session.v1";
        const float BoardX = 64, BoardY = 312, Cell = 74;
        static readonly Color Ink = new Color(.025f,.07f,.23f);
        static readonly Color Cream = new Color(.92f,.96f,1);
        static readonly Color[] Palette = { Color.clear,
            new Color(.18f,.68f,1), new Color(1,.40f,.45f), new Color(1,.77f,.16f), new Color(.36f,.88f,.55f),
            new Color(.71f,.42f,1), new Color(1,.52f,.19f), new Color(.13f,.87f,.87f), new Color(.96f,.35f,.73f) };
        RectTransform root, content, board, modal, drag, effects;
        Font font; Sprite rounded; Texture2D roundTexture;
        AudioSource speaker; AudioClip tap, place, clear, fail;
        readonly List<AudioClip> ownedClips = new List<AudioClip>();
        Image[] cells = new Image[64];
        RectTransform[] slots = new RectTransform[3];
        Text scoreText, bestText, statusText, comboText, progressText;
        Image progressFill;
        Button[] powerButtons = new Button[3];
        int dragging = -1, armed, best, adventureLevel;
        Vector2 dragOrigin, dragAnchor;
        int hoverX = -99, hoverY = -99;
        bool sound, music, vibration, paused, isClearing, ready;
        float toastUntil, nextMusic;
        Text toast;
        string savedSession;
        bool smokeMode;
        [SerializeField, HideInInspector] string reloadState;
        [SerializeField, HideInInspector] string reloadScreen;

        void OnEnable()
        {
            // Unity keeps runtime UI objects during a script reload but resets nonserialized fields.
            // Rebuild owned UI and restore the last state saved by OnDisable.
            if(!Application.isPlaying || ready || !transform.Find("Block Blast UI"))return;
            BlastState state=null;
            try {if(!string.IsNullOrEmpty(reloadState))state=JsonUtility.FromJson<BlastState>(reloadState);}catch{ }
            bool wasGame=reloadScreen=="Game";
            var oldUI=transform.Find("Block Blast UI");DestroyImmediate(oldUI.gameObject);
            Start();
            if(wasGame && Model.Restore(state)) {GameScreen();if(Model.State.ended)EndScreen();}
        }

        void Start()
        {
            if(ready)return;
            smokeMode=Array.IndexOf(Environment.GetCommandLineArgs(),"--blast-smoke-test")>=0;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = CreateRounded();
            speaker = GetComponent<AudioSource>();if(!speaker)speaker=gameObject.AddComponent<AudioSource>();
            speaker.Stop();speaker.playOnAwake = false;
            tap = Tone(650,.07f); place = Tone(330,.13f); clear = Tone(880,.34f); fail = Tone(160,.20f);
            Tone(220,.60f);Tone(277.18f,.60f);Tone(329.63f,.60f);Tone(440,.60f);
            sound = PlayerPrefs.GetInt("Blast.Sound",1) == 1;
            music = PlayerPrefs.GetInt("Blast.Music",0) == 1;
            vibration = PlayerPrefs.GetInt("Blast.Vibration",0) == 1;
            best = PlayerPrefs.GetInt("Blast.Best",0); adventureLevel = PlayerPrefs.GetInt("Blast.Level",1);
            savedSession = PlayerPrefs.GetString(SaveKey, "");
            Model = new BlastModel(Environment.TickCount);
            BuildCanvas(); ready = true; Home();
            if(smokeMode)StartCoroutine(StandaloneSmoke());
        }
        void BuildCanvas()
        {
            var canvasGO = new GameObject("Block Blast UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform,false);
            var canvas = canvasGO.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720,1280); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = new GameObject("Letterbox",typeof(RectTransform),typeof(Image)); backdrop.transform.SetParent(canvasGO.transform,false);
            var full = backdrop.GetComponent<RectTransform>(); full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one; full.offsetMin = full.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = Ink; backdrop.GetComponent<Image>().raycastTarget = false;
            root = new GameObject("Portrait 720 x 1280",typeof(RectTransform),typeof(RectMask2D)).GetComponent<RectTransform>();
            root.SetParent(canvasGO.transform,false); root.anchorMin = root.anchorMax = new Vector2(.5f,.5f); root.sizeDelta = new Vector2(720,1280);
            var background = Rect(root,"Figma • Asset 23-8",0,0,720,1480);
            var raw = background.gameObject.AddComponent<RawImage>(); raw.texture = Resources.Load<Texture2D>("Art/FigmaBackground"); raw.raycastTarget = false;
            Image(root,"Tint",0,0,720,1280,new Color(.025f,.055f,.19f,.35f),false);
            if (FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
        }
        void ResetScreen(string name)
        {
            StopAllCoroutines(); isClearing = false; dragging = -1; armed = 0; paused = false;
            if (content) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
            CloseModal();
            if (drag) Destroy(drag.gameObject);
            ScreenName = name; content = Rect(root,name,0,0,720,1280); effects = Rect(content,"Effects",0,0,720,1280);
            toast = null; modal = null;
        }
        public void Home()
        {
            if (ready && ScreenName == "Game") Save();
            ResetScreen("Home");
            Label(content,"BLOCK",55,153,610,109,85,Cream,TextAnchor.MiddleCenter,true);
            Label(content,"BLAST",55,247,610,115,90,new Color(1,.79f,.24f),TextAnchor.MiddleCenter,true);
            Label(content,"XẾP KHỐI • PHÁ KỶ LỤC",40,367,640,40,22,new Color(.68f,.82f,1),TextAnchor.MiddleCenter);
            // Decorative native blocks. Replace these and the board tiles after the remaining Figma exports are available.
            var hero = Rect(content,"Native block motif",155,455,410,220);
            int[,] motif = {{0,0,1},{1,0,1},{2,0,2},{0,1,1},{1,1,3},{2,1,2},{3,1,2},{1,2,3},{2,2,3},{3,2,4},{4,2,4}};
            for (int i=0;i<motif.GetLength(0);i++) Tile(hero,motif[i,0]*76,motif[i,1]*76,70,Palette[motif[i,2]]);
            Label(content,"KỶ LỤC   " + best.ToString("N0"),55,719,610,55,31,Cream,TextAnchor.MiddleCenter,true);
            bool resume = TryReadSession(out var session);
            Button(content,"Classic",100,805,520,88,resume ? "TIẾP TỤC CLASSIC" : "CHƠI CLASSIC",new Color(.24f,.75f,.53f),()=>{ if(resume) Resume(); else StartGame(false); });
            if(resume) Button(content,"New Classic",204,903,312,42,"VÁN CLASSIC MỚI",new Color(.13f,.28f,.63f),()=>ConfirmNew());
            Button(content,"Adventure",100,resume?965:918,520,88,"ADVENTURE  ·  LEVEL " + adventureLevel,new Color(1,.68f,.18f),()=>StartGame(true));
            Button(content,"How to play",100,1087,252,60,"LUẬT CHƠI",new Color(.16f,.32f,.66f),()=>ShowRules());
            Button(content,"Settings",368,1087,252,60,"CÀI ĐẶT",new Color(.16f,.32f,.66f),()=>ShowSettings());
            Label(content,"Không giới hạn thời gian. Mỗi nước đi đều quan trọng.",60,1190,600,35,19,new Color(.64f,.77f,.96f),TextAnchor.MiddleCenter);
            effects.SetAsLastSibling();
        }
        void ConfirmNew()
        {
            OpenModal("VÁN MỚI", "Ván Classic đang lưu sẽ được thay thế.", 450);
            Button(modal,"New game",70,270,440,74,"BẮT ĐẦU",new Color(.23f,.74f,.51f),()=>StartGame(false));
            Button(modal,"Cancel",70,360,440,56,"QUAY LẠI",new Color(.16f,.32f,.66f),CloseModal);
        }
        public void StartGame(bool adventure)
        {
            Model.NewGame(adventure,adventureLevel);
            if (!adventure) { if(!smokeMode)PlayerPrefs.DeleteKey(SaveKey); savedSession = ""; }
            GameScreen(); Save();
        }
        public void Resume()
        {
            if (TryReadSession(out var state) && Model.Restore(state)) { GameScreen(); Model.Evaluate(); Render(); if(Model.State.ended) EndScreen(); }
            else StartGame(false);
        }
        bool TryReadSession(out BlastState state)
        {
            state = null;
            try { if(string.IsNullOrEmpty(savedSession)) return false; state = JsonUtility.FromJson<BlastState>(savedSession);
                var check = new BlastModel(1); return check.Restore(state) && !state.adventure && !state.ended; }
            catch { return false; }
        }
        void GameScreen()
        {
            ResetScreen("Game");
            Button(content,"Home",42,48,106,54,"HOME",new Color(.13f,.27f,.56f),Home);
            Button(content,"Pause",572,48,106,54,"PAUSE",new Color(.13f,.27f,.56f),ShowPause);
            Label(content,Model.State.adventure?"ADVENTURE":"CLASSIC",169,46,382,55,26,Cream,TextAnchor.MiddleCenter,true);
            Label(content,"ĐIỂM",180,126,360,27,18,new Color(.62f,.78f,1),TextAnchor.MiddleCenter);
            scoreText = Label(content,"0",120,151,480,89,70,Cream,TextAnchor.MiddleCenter,true);
            bestText = Label(content,"KỶ LỤC  0",35,247,340,37,21,new Color(1,.8f,.28f),TextAnchor.MiddleLeft,true);
            comboText = Label(content,"",381,247,304,37,23,new Color(1,.8f,.28f),TextAnchor.MiddleRight,true);
            Image(content,"Board shadow",56,306,608,608,new Color(.015f,.03f,.12f,.55f));
            board = Rect(content,"8 x 8 Board",BoardX,BoardY,592,592);
            Image(board,"Board base",0,0,592,592,new Color(.032f,.09f,.24f,.97f));
            for(int y=0;y<8;y++) for(int x=0;x<8;x++)
            {
                int index = y*8+x;
                cells[index]=Image(board,"Cell "+x+","+y,x*Cell+3,y*Cell+3,68,68,new Color(.065f,.16f,.34f),false);
                var bt=cells[index].gameObject.AddComponent<Button>(); bt.targetGraphic=cells[index];
                cells[index].raycastTarget=true; int cx=x,cy=y; bt.onClick.AddListener(()=>UsePower(cx,cy));
            }
            progressText=Label(content,"",64,284,592,25,17,Cream,TextAnchor.MiddleCenter);
            var progressBack=Image(content,"Target track",64,916,592,8,new Color(.025f,.07f,.21f));
            progressFill=Image(progressBack.rectTransform,"Target progress",0,0,0,8,new Color(1,.77f,.19f));
            for(int i=0;i<3;i++)
            {
                slots[i]=Rect(content,"Tray "+(i+1),46+i*216,963,196,179);
                Image(slots[i],"Slot base",0,0,196,179,new Color(.04f,.13f,.33f,.64f),false);
                var hit=Image(slots[i],"Drag area",0,0,196,179,Color.clear,true);
                var handle=hit.gameObject.AddComponent<BlastDrag>(); handle.game=this; handle.slot=i;
            }
            powerButtons[0]=Button(content,"Hammer",64,1151,181,62,"BÚA  ·  1",new Color(.17f,.31f,.62f),()=>ArmPower(1));
            powerButtons[1]=Button(content,"Bomb",269,1151,181,62,"BOM  ·  1",new Color(.17f,.31f,.62f),()=>ArmPower(2));
            powerButtons[2]=Button(content,"Shuffle",474,1151,181,62,"ĐỔI  ·  1",new Color(.17f,.31f,.62f),Shuffle);
            statusText=Label(content,"Kéo khối vào bảng để bắt đầu",45,930,630,27,18,new Color(.75f,.86f,1),TextAnchor.MiddleCenter);
            Label(content,"Lấp đầy hàng hoặc cột để xóa • Không xoay khối",35,1224,650,26,17,new Color(.59f,.74f,.95f),TextAnchor.MiddleCenter);
            effects.SetAsLastSibling(); Render();
        }
        void Render()
        {
            if(ScreenName!="Game") return;
            var s=Model.State;
            for(int i=0;i<64;i++) { cells[i].color=s.cells[i]==0?new Color(.065f,.16f,.34f):Palette[s.cells[i]];
                var bevel=cells[i].transform.Find("Bevel"); if(bevel) Destroy(bevel.gameObject);
                if(s.cells[i]!=0) Bevel(cells[i].rectTransform,68,Palette[s.cells[i]]);
            }
            for(int i=0;i<3;i++)
            {
                var old=slots[i].Find("Piece"); if(old) {old.gameObject.SetActive(false);Destroy(old.gameObject);}
                if(s.tray[i]<0) continue;
                var shape=BlastModel.Shapes[s.tray[i]]; Vector2 size=ShapeSize(shape);
                float scale=Mathf.Min(35,150/Mathf.Max(size.x,size.y));
                var piece=Rect(slots[i],"Piece",(196-size.x*scale)*.5f,(179-size.y*scale)*.5f,size.x*scale,size.y*scale);
                Color col=Palette[s.colors[i]]; if(!Model.HasFit(s.tray[i])) col=Color.Lerp(col,Ink,.64f);
                foreach(var p in shape) Tile(piece,p.x*scale,p.y*scale,scale-2,col);
            }
            scoreText.text=s.score.ToString("N0"); bestText.text="KỶ LỤC  "+best.ToString("N0");
            comboText.text=s.combo>0?"COMBO ×"+s.combo+(s.misses>0?"  · "+(3-s.misses):""):"";
            progressText.text=s.adventure?"LEVEL "+s.level+"     MỤC TIÊU "+Model.Target+"     CÒN "+s.moves+" LƯỢT":"";
            progressFill.rectTransform.sizeDelta=new Vector2(s.adventure?592*Mathf.Clamp01((float)s.score/Model.Target):0,8);
            string[] labels={"BÚA  ·  "+s.hammer,"BOM  ·  "+s.bomb,"ĐỔI  ·  "+s.shuffle};
            int[] counts={s.hammer,s.bomb,s.shuffle};
            for(int i=0;i<3;i++) { powerButtons[i].GetComponentInChildren<Text>().text=labels[i]; powerButtons[i].interactable=counts[i]>0; }
        }
        public bool BeginDrag(int slot, Vector2 screenPoint)
        {
            if(ScreenName!="Game" || paused || modal || isClearing || dragging>=0 || Model.State.ended || Model.State.tray[slot]<0) return false;
            armed=0; dragging=slot; var shape=BlastModel.Shapes[Model.State.tray[slot]];
            Vector2 size=ShapeSize(shape);
            dragAnchor=new Vector2(size.x*Cell*.5f,size.y*Cell*.5f);
            drag=Rect(root,"Dragged piece",0,0,size.x*Cell,size.y*Cell);
            foreach(var p in shape) Tile(drag,p.x*Cell+3,p.y*Cell+3,68,Palette[Model.State.colors[slot]]);
            slots[slot].GetComponentInChildren<BlastDrag>().GetComponent<Image>().color=Color.clear;
            var piece=slots[slot].Find("Piece"); if(piece) piece.gameObject.SetActive(false);
            Play(tap); UpdateDrag(screenPoint);
            return true;
        }
        public void UpdateDrag(Vector2 screenPoint)
        {
            if(dragging<0 || !drag) return;
            Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screenPoint,null,out local);
            Vector2 point=new Vector2(local.x+360,640-local.y);
            // The piece sits above a touch so the board preview stays visible.
            if(Input.touchCount>0) point.y-=85;
            dragOrigin=point-dragAnchor;
            drag.anchoredPosition=new Vector2(dragOrigin.x,-dragOrigin.y);
            hoverX=Mathf.RoundToInt((dragOrigin.x-BoardX)/Cell); hoverY=Mathf.RoundToInt((dragOrigin.y-BoardY)/Cell);
            Preview();
        }
        void Preview()
        {
            var s=Model.State;
            for(int i=0;i<64;i++) cells[i].color=s.cells[i]==0?new Color(.065f,.16f,.34f):Palette[s.cells[i]];
            bool valid=Model.CanPlace(s.tray[dragging],hoverX,hoverY);
            foreach(var p in BlastModel.Shapes[s.tray[dragging]])
            {
                int x=hoverX+p.x,y=hoverY+p.y;
                if(x>=0&&x<8&&y>=0&&y<8) cells[y*8+x].color=valid?Color.Lerp(Palette[s.colors[dragging]],Color.white,.40f):new Color(.85f,.19f,.28f);
            }
            if(valid) foreach(int i in Model.PreviewLines(s.tray[dragging],hoverX,hoverY)) cells[i].color=new Color(1,.9f,.49f);
            statusText.text=valid?"Thả để đặt khối":"Đặt khối vào các ô trống";
        }
        public void EndDrag(Vector2 screenPoint)
        {
            if(dragging<0) return;
            UpdateDrag(screenPoint); int slot=dragging; dragging=-1;
            if(drag) {Destroy(drag.gameObject);drag=null;}
            if(!TryPlace(slot,hoverX,hoverY)) {Play(fail);Render();statusText.text="Vị trí chưa hợp lệ — thử chỗ khác";}
        }
        public bool TryPlace(int slot,int x,int y)
        {
            if(ScreenName!="Game"||paused||modal||isClearing) return false;
            int previousScore=Model.State.score;
            List<int> cleared; int lines;
            if(!Model.Place(slot,x,y,out cleared,out lines)) return false;
            UpdateBest(); Save(); Render();
            if(lines>0) {Play(clear);StartCoroutine(Blast(cleared));Notify(lines+" HÀNG/CỘT  ·  +"+(Model.State.score-previousScore),new Color(1,.85f,.34f));}
            else {Play(place);statusText.text="Dùng hết 3 khối để nhận bộ mới";}
            #if UNITY_ANDROID || UNITY_IOS
            if(vibration) Handheld.Vibrate();
            #endif
            if(Model.State.ended) StartCoroutine(EndAfterEffects());
            return true;
        }
        IEnumerator EndAfterEffects() { yield return new WaitForSecondsRealtime(.52f); EndScreen(); }
        IEnumerator Blast(List<int> indices)
        {
            isClearing=true;
            foreach(int index in indices)
            {
                int x=index%8,y=index/8;
                var particle=Image(effects,"Blast sparkle",BoardX+x*Cell+18,BoardY+y*Cell+18,38,38,new Color(1,.89f,.37f));
                StartCoroutine(Fly(particle,new Vector2(UnityEngine.Random.Range(-90f,90f),UnityEngine.Random.Range(45f,165f))));
            }
            yield return new WaitForSecondsRealtime(.35f);isClearing=false;
        }
        IEnumerator Fly(Image particle,Vector2 velocity)
        {
            float t=0;Vector2 start=particle.rectTransform.anchoredPosition;
            while(t<.5f && particle)
            {
                t+=Time.unscaledDeltaTime;particle.rectTransform.anchoredPosition=start+velocity*t+Vector2.down*350*t*t;
                particle.rectTransform.localScale=Vector3.one*(1-t/.5f);yield return null;
            }
            if(particle)Destroy(particle.gameObject);
        }
        void UpdateBest()
        {
            if(Model.State.adventure || Model.State.score<=best) return;
            best=Model.State.score;if(!smokeMode)PlayerPrefs.SetInt("Blast.Best",best);
        }
        void ArmPower(int which)
        {
            if(isClearing||dragging>=0||Model.State.won) return;
            armed=armed==which?0:which;
            statusText.text=armed==1?"BÚA: chạm vào một ô có khối để xóa":armed==2?"BOM: chạm bảng để xóa vùng 3×3":"Kéo khối vào bảng";
            Play(tap);
        }
        public void UsePower(int x,int y)
        {
            if(armed==0 || paused || modal || isClearing || dragging>=0) return;
            var removed=Model.Erase(x,y,armed==2);
            if(removed.Count==0) {Notify("Chọn vùng có khối",Cream);return;}
            armed=0;Play(clear);Render();Save();StartCoroutine(Blast(removed));
            statusText.text="Đã tạo thêm chỗ trống";
            if(Model.State.ended)StartCoroutine(EndAfterEffects());
        }
        public void Shuffle()
        {
            if(isClearing||dragging>=0||paused||Model.State.won) return;
            if(!Model.Shuffle()) return;
            CloseModal();armed=0;Render();Save();Play(tap);Notify("BỘ KHỐI MỚI",Cream);
            if(Model.State.ended)StartCoroutine(EndAfterEffects());
        }
        void EndScreen()
        {
            if(ScreenName!="Game"||!Model.State.ended) return;
            CloseModal(); var s=Model.State;
            if(s.won)
            {
                adventureLevel=Mathf.Max(adventureLevel,Mathf.Min(99,s.level+1));PlayerPrefs.SetInt("Blast.Level",adventureLevel);PlayerPrefs.Save();
                OpenModal("YOU WIN!","Đã hoàn thành Level "+s.level+"\n"+s.score.ToString("N0")+" điểm · còn "+s.moves+" lượt",570);
                Button(modal,"Next Level",70,278,440,76,"LEVEL TIẾP THEO",new Color(.24f,.75f,.53f),()=>StartGame(true));
            }
            else
            {
                bool rescue=!(s.adventure&&s.moves<=0) && (s.bomb>0||s.hammer>0||s.shuffle>0);
                OpenModal("GAME OVER",(s.adventure&&s.moves<=0?"Bạn đã hết lượt.":"Không còn chỗ cho các khối trong khay.")+"\nĐiểm của bạn: "+s.score.ToString("N0"),rescue?670:570);
                if(rescue) Button(modal,"Rescue",70,278,440,70,"DÙNG TRỢ GIÚP",new Color(.24f,.75f,.53f),()=>{
                    CloseModal();Render();
                    if(s.bomb>0)ArmPower(2);else if(s.hammer>0)ArmPower(1);else Shuffle();
                });
                Button(modal,"Retry",70,rescue?364:278,440,76,"CHƠI LẠI",new Color(1,.70f,.20f),()=>StartGame(s.adventure));
            }
            Button(modal,"Home",70,modal.sizeDelta.y-111,440,64,"VỀ TRANG CHỦ",new Color(.17f,.32f,.63f),Home);
        }
        void ShowPause()
        {
            if(dragging>=0 || isClearing || Model.State.ended) return;
            OpenModal("TẠM DỪNG","Ván chơi được lưu tự động.",580);
            Button(modal,"Continue",70,255,440,74,"TIẾP TỤC",new Color(.24f,.75f,.53f),CloseModal);
            Button(modal,"Settings",70,345,440,64,"CÀI ĐẶT",new Color(.17f,.32f,.63f),ShowSettings);
            Button(modal,"Home",70,425,440,64,"VỀ TRANG CHỦ",new Color(.17f,.32f,.63f),Home);
        }
        void ShowSettings()
        {
            CloseModal();OpenModal("CÀI ĐẶT","Tùy chỉnh trải nghiệm chơi",650);
            Button musicButton=null,soundButton=null,vibrationButton=null;
            soundButton=Button(modal,"Sound",70,245,440,68,"ÂM THANH: "+(sound?"BẬT":"TẮT"),new Color(.17f,.32f,.63f),()=>{
                sound=!sound;PlayerPrefs.SetInt("Blast.Sound",sound?1:0);soundButton.GetComponentInChildren<Text>().text="ÂM THANH: "+(sound?"BẬT":"TẮT");Play(tap);PlayerPrefs.Save();
            });
            musicButton=Button(modal,"Music",70,330,440,68,"NHẠC NỀN: "+(music?"BẬT":"TẮT"),new Color(.17f,.32f,.63f),()=>{
                music=!music;PlayerPrefs.SetInt("Blast.Music",music?1:0);musicButton.GetComponentInChildren<Text>().text="NHẠC NỀN: "+(music?"BẬT":"TẮT");PlayerPrefs.Save();
            });
            vibrationButton=Button(modal,"Vibration",70,415,440,68,"RUNG: "+(vibration?"BẬT":"TẮT"),new Color(.17f,.32f,.63f),()=>{
                vibration=!vibration;PlayerPrefs.SetInt("Blast.Vibration",vibration?1:0);vibrationButton.GetComponentInChildren<Text>().text="RUNG: "+(vibration?"BẬT":"TẮT");PlayerPrefs.Save();
            });
            Label(modal,"Rung chỉ hoạt động trên thiết bị di động.",45,495,490,35,18,new Color(.65f,.77f,.95f),TextAnchor.MiddleCenter);
            Button(modal,"Close",70,550,440,64,"XONG",new Color(.24f,.75f,.53f),CloseModal);
        }
        void ShowRules()
        {
            OpenModal("LUẬT CHƠI","",930);
            string rules="01  Kéo một khối từ khay vào ô trống.\nKhối phải nằm trọn trong bảng 8×8. Không xoay.\n\n02  Lấp đầy hàng hoặc cột để xóa.\nDùng hết 3 khối để nhận bộ mới.\n\n03  Mỗi ô đặt được +10 điểm.\nXóa dòng: 80 × số dòng² × combo (tối đa ×10).\nXóa sạch bảng được thêm 300 điểm.\n\n04  Combo tăng khi xóa dòng.\nBa lần đặt liên tiếp không xóa sẽ mất combo.\n\n05  Búa xóa 1 ô. Bom xóa vùng 3×3.\nĐổi thay cả bộ khối. Mỗi trợ giúp dùng 1 lần/ván.\n\n06  Classic: chơi đến khi hết chỗ.\nAdventure: đạt mục tiêu trong 30 lượt.\nHết chỗ có thể dùng trợ giúp còn lại để cứu ván.";
            Label(modal,rules,44,190,492,630,21,Cream,TextAnchor.UpperLeft);
            Button(modal,"Close",70,829,440,62,"ĐÃ HIỂU",new Color(.24f,.75f,.53f),CloseModal);
        }
        void OpenModal(string title,string description,float height)
        {
            if(modal)CloseModal();paused=true;Play(tap);
            var overlay=Image(root,"Modal shade",0,0,720,1280,new Color(.005f,.02f,.08f,.86f),true);
            modal=Rect(overlay.rectTransform,"Dialog",70,(1280-height)*.5f,580,height);
            Image(modal,"Panel",0,0,580,height,new Color(.055f,.13f,.31f),true);
            Image(modal,"Accent",44,38,492,6,new Color(1,.77f,.24f));
            Label(modal,title,35,67,510,75,44,Cream,TextAnchor.MiddleCenter,true);
            Label(modal,description,44,151,492,104,23,new Color(.75f,.85f,1),TextAnchor.MiddleCenter);
        }
        void CloseModal()
        {
            if(modal) {modal.parent.gameObject.SetActive(false);Destroy(modal.parent.gameObject);modal=null;}
            paused=false;
        }
        void Notify(string message,Color color)
        {
            if(!toast) toast=Label(effects,"",55,206,610,60,27,color,TextAnchor.MiddleCenter,true);
            toast.text=message;toast.color=color;toastUntil=Time.unscaledTime+1.5f;
            toast.transform.SetAsLastSibling();
        }
        void Update()
        {
            if(!ready)return;
            if(toast&&Time.unscaledTime>toastUntil)toast.text="";
            if(music&&Time.unscaledTime>nextMusic)
            {
                nextMusic=Time.unscaledTime+.72f;
                if(!paused) speaker.PlayOneShot(ownedClips[4+(Time.frameCount/43)%4],.065f);
            }
            if(Input.GetKeyDown(KeyCode.Escape)) {if(modal)CloseModal();else if(ScreenName=="Game")ShowPause();}
            if(dragging>=0 && Input.touchCount==0 && !Input.GetMouseButton(0)) EndDrag(Input.mousePosition);
        }
        public void Save()
        {
            if(Model==null || ScreenName!="Game")return;
            if(!Model.State.adventure)
            {
                if(Model.State.ended){savedSession="";if(!smokeMode)PlayerPrefs.DeleteKey(SaveKey);}
                else{savedSession=JsonUtility.ToJson(Model.State);if(!smokeMode)PlayerPrefs.SetString(SaveKey,savedSession);}
            }
            if(!smokeMode)PlayerPrefs.Save();
        }
        IEnumerator StandaloneSmoke()
        {
            yield return null;
            try
            {
                if(!Resources.Load<Texture2D>("Art/FigmaBackground")||!font)throw new Exception("Packaged art or font missing");
                if(ScreenName!="Home"||GetComponentsInChildren<Button>().Length<4)throw new Exception("Home UI missing");
                StartGame(false);
                if(!TryPlace(0,0,0)||Model.State.score<=0)throw new Exception("Standalone placement failed");
                int points=Model.State.score;Home();Resume();
                if(ScreenName!="Game"||Model.State.score!=points)throw new Exception("Standalone save/resume failed");
                Debug.Log("BLOCK_BLAST_STANDALONE_SMOKE_OK: packaged art, font, home, placement, score, save/resume");
                Application.Quit(0);
            }
            catch(Exception error){Debug.LogError("BLOCK_BLAST_STANDALONE_SMOKE_FAILED: "+error);Application.Quit(1);}
        }
        void CancelDrag()
        {
            if(dragging<0)return;
            dragging=-1;if(drag){Destroy(drag.gameObject);drag=null;}Render();
        }
        void OnApplicationFocus(bool value){if(!value && ready){CancelDrag();Save();}}
        void OnApplicationPause(bool value){if(value && ready){CancelDrag();Save();}}
        void OnApplicationQuit(){if(ready)Save();}
        void OnDisable()
        {
            if(ready){CancelDrag();reloadState=JsonUtility.ToJson(Model.State);reloadScreen=ScreenName;Save();}
        }
        void OnDestroy()
        {
            if(rounded)Destroy(rounded);if(roundTexture)Destroy(roundTexture);
            foreach(var clip in ownedClips)if(clip)Destroy(clip);
        }
        AudioClip Tone(float frequency,float duration)
        {
            int length=(int)(22050*duration);var data=new float[length];
            for(int i=0;i<length;i++){float t=(float)i/22050;float env=Mathf.Sin(Mathf.PI*i/length);data[i]=Mathf.Sin(2*Mathf.PI*frequency*t)*env*.3f;}
            var clip=AudioClip.Create("Blast tone "+frequency,length,1,22050,false);clip.SetData(data,0);ownedClips.Add(clip);
            return clip;
        }
        void Play(AudioClip clip){if(sound && clip)speaker.PlayOneShot(clip,.6f);}
        Sprite CreateRounded()
        {
            roundTexture=new Texture2D(32,32,TextureFormat.RGBA32,false);roundTexture.filterMode=FilterMode.Bilinear;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float dx=Mathf.Max(8-x,Mathf.Max(x-23,0));float dy=Mathf.Max(8-y,Mathf.Max(y-23,0));
                roundTexture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(8.5f-Mathf.Sqrt(dx*dx+dy*dy))));
            }
            roundTexture.Apply();
            return Sprite.Create(roundTexture,new Rect(0,0,32,32),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(9,9,9,9));
        }
        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);return rt;
        }
        Image Image(Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false)
        {
            var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=raycast;
            image.sprite=rounded;image.type=UnityEngine.UI.Image.Type.Sliced;return image;
        }
        Text Label(Transform parent,string value,float x,float y,float w,float h,int size,Color color,TextAnchor alignment,bool bold=false)
        {
            var text=Rect(parent,string.IsNullOrEmpty(value)?"Label":value,x,y,w,h).gameObject.AddComponent<Text>();
            text.font=font;text.text=value;text.fontSize=size;text.color=color;text.alignment=alignment;
            text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            return text;
        }
        Button Button(Transform parent,string name,float x,float y,float w,float h,string label,Color color,UnityEngine.Events.UnityAction action)
        {
            Image(parent,name+" shadow",x,y+5,w,h,Color.Lerp(color,Ink,.6f));
            var im=Image(parent,name,x,y,w,h,color,true);var button=im.gameObject.AddComponent<Button>();button.targetGraphic=im;
            var cb=button.colors;cb.highlightedColor=new Color(1.1f,1.1f,1.1f);cb.pressedColor=new Color(.75f,.75f,.75f);cb.disabledColor=new Color(.5f,.5f,.5f,.55f);button.colors=cb;
            Label(im.transform,label,10,0,w-20,h,h>70?27:20,Cream,TextAnchor.MiddleCenter,true);
            button.onClick.AddListener(action);return button;
        }
        void Tile(Transform parent,float x,float y,float size,Color color)
        {
            var im=Image(parent,"Native tile",x,y,size,size,color,false);Bevel(im.rectTransform,size,color);
        }
        void Bevel(RectTransform parent,float size,Color color)
        {
            var bevel=Rect(parent,"Bevel",0,0,size,size);
            Image(bevel,"Top",4,3,size-8,Mathf.Max(3,size*.09f),Color.Lerp(color,Color.white,.42f));
            Image(bevel,"Bottom",4,size-Mathf.Max(5,size*.12f),size-8,Mathf.Max(3,size*.08f),Color.Lerp(color,Ink,.31f));
            Image(bevel,"Gleam",5,8,Mathf.Max(2,size*.04f),size*.42f,new Color(1,1,1,.18f));
        }
        static Vector2 ShapeSize(Vector2Int[] shape)
        {
            int x=0,y=0;foreach(var p in shape){x=Math.Max(x,p.x+1);y=Math.Max(y,p.y+1);}return new Vector2(x,y);
        }
    }
    public sealed class BlastDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public BlastGame game;public int slot;
        int pointer=int.MinValue;
        public void OnPointerDown(PointerEventData e){if(game.BeginDrag(slot,e.position))pointer=e.pointerId;}
        public void OnBeginDrag(PointerEventData e){if(e.pointerId==pointer)game.UpdateDrag(e.position);}
        public void OnDrag(PointerEventData e){if(e.pointerId==pointer)game.UpdateDrag(e.position);}
        public void OnEndDrag(PointerEventData e){if(e.pointerId==pointer){game.EndDrag(e.position);pointer=int.MinValue;}}
        public void OnPointerUp(PointerEventData e){if(e.pointerId==pointer){game.EndDrag(e.position);pointer=int.MinValue;}}
    }
}
