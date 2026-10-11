using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BlockBlast.Editor
{
    public static class BlastEditor
    {
        [MenuItem("Block Blast/Set up playable scene")]
        public static void Setup()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before setup.");
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.isDirty)EditorSceneManager.SaveScene(scene);
            var game=UnityEngine.Object.FindObjectOfType<BlastGame>();
            if(!game)new GameObject("Block Blast Game").AddComponent<BlastGame>();
            var camera=Camera.main;
            if(!camera)camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.07f,.23f);
            camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);
            PlayerSettings.productName="Block Blast";
            PlayerSettings.defaultScreenWidth=720;PlayerSettings.defaultScreenHeight=1000;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            PlayerSettings.runInBackground=false;
            var importer=AssetImporter.GetAtPath("Assets/BlockBlast/Resources/Art/FigmaBackground.png") as TextureImporter;
            if(importer){importer.textureType=TextureImporterType.Default;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.SaveAndReimport();}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scene.path,true)};
            AssetDatabase.SaveAssets();Debug.Log("BLOCK_BLAST_SETUP_OK: "+scene.path);
        }
        static void Require(bool value,string name){if(!value)throw new Exception("FAILED: "+name);}
        static BlastState Empty(){return new BlastState{tray=new[]{0,0,0},colors=new[]{1,2,3}};}
        [MenuItem("Block Blast/Run rule checks")]
        public static string RuleChecks()
        {
            int checks=0;var model=new BlastModel(42);var state=Empty();Require(model.Restore(state),"restore valid");checks++;
            Require(!model.CanPlace(0,-1,0)&&!model.CanPlace(0,8,0),"bounds");checks++;
            state.cells[0]=2;Require(!model.CanPlace(0,0,0),"overlap");checks++;
            List<int> erased;int lines;int before=state.score;
            Require(!model.Place(0,0,0,out erased,out lines)&&state.score==before&&state.tray[0]==0,"invalid drop does not mutate");checks++;
            // Completing one intersecting row and column must remove 15 unique cells simultaneously.
            state=Empty();for(int i=0;i<8;i++){state.cells[i]=1;state.cells[i*8]=1;}state.cells[0]=0;model.Restore(state);
            Require(model.Place(0,0,0,out erased,out lines)&&lines==2&&erased.Count==15,"simultaneous clear");checks++;
            Require(state.score==630&&state.combo==1,"score: cell + two lines + all clear");checks++;
            Require(model.Place(1,0,0,out erased,out lines)&&model.Place(2,1,0,out erased,out lines),"remaining tray");checks++;
            Require(state.tray[0]>=0&&state.tray[1]>=0&&state.tray[2]>=0,"three piece refill");checks++;
            // Combo resets after exactly three placements without a clear.
            state=Empty();state.combo=4;model.Restore(state);
            model.Place(0,0,0,out erased,out lines);Require(state.combo==4&&state.misses==1,"combo grace 1");checks++;
            model.Place(1,1,0,out erased,out lines);Require(state.combo==4&&state.misses==2,"combo grace 2");checks++;
            model.Place(2,2,0,out erased,out lines);Require(state.combo==0&&state.misses==0,"combo reset");checks++;
            state=Empty();for(int i=0;i<64;i++)state.cells[i]=1;model.Restore(state);model.Evaluate();Require(state.ended&&!model.AnyMove(),"dead board game over");checks++;
            Require(model.Erase(3,3,true).Count==9&&state.bomb==0&&!state.ended,"bomb rescues dead board");checks++;
            Require(model.Erase(3,3,true).Count==0,"power is single use");checks++;
            state=Empty();model.Restore(state);Require(model.Erase(0,0,false).Count==0&&state.hammer==1,"empty hammer preserves token");checks++;
            state.cells[0]=2;Require(model.Erase(0,0,false).Count==1&&state.hammer==0,"hammer consumes one token");checks++;
            Require(model.Shuffle()&&state.shuffle==0&&!model.Shuffle(),"shuffle single use");checks++;
            state=Empty();state.adventure=true;state.moves=1;model.Restore(state);model.Place(0,0,0,out erased,out lines);
            Require(state.ended&&!state.won&&state.moves==0,"adventure move limit");checks++;
            state=Empty();state.adventure=true;state.moves=1;state.score=640;model.Restore(state);model.Place(0,0,0,out erased,out lines);
            Require(state.won&&state.ended,"target wins on last move");checks++;
            var json=JsonUtility.ToJson(state);Require(model.Restore(JsonUtility.FromJson<BlastState>(json)),"save round trip");checks++;
            state=Empty();state.cells=new int[5];Require(!model.Restore(state),"reject corrupted save");checks++;
            // All shapes are normalized, unique within their shape, connected, and fit on an empty board.
            state=Empty();model.Restore(state);
            for(int s=0;s<BlastModel.Shapes.Length;s++)
            {
                var shape=BlastModel.Shapes[s];var unique=new HashSet<Vector2Int>(shape);
                Require(unique.Count==shape.Length&&model.HasFit(s),"shape "+s);
            }
            checks++;
            // Deterministic legal-play simulation covers refill, scoring and end conditions over many games.
            int placements=0;
            for(int seed=0;seed<40;seed++)
            {
                var game=new BlastModel(seed);
                for(int turn=0;turn<300&&!game.State.ended;turn++)
                {
                    bool moved=false;
                    for(int slot=0;slot<3&&!moved;slot++)for(int y=0;y<8&&!moved;y++)for(int x=0;x<8&&!moved;x++)
                        if(game.CanPlace(game.State.tray[slot],x,y)){Require(game.Place(slot,x,y,out erased,out lines),"legal simulation placement");placements++;moved=true;}
                    Require(moved||game.State.ended,"simulation progression");
                    foreach(int c in game.State.cells)Require(c>=0&&c<=8,"simulation cell range");
                    Require(game.State.score>=0,"simulation score");
                }
            }
            checks++;
            string result="BLOCK_BLAST_RULES_OK: "+checks+" checks; "+placements+" simulated placements; "+BlastModel.Shapes.Length+" shapes.";
            Debug.Log(result);return result;
        }
    }
}
