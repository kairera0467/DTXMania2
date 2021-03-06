﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectInput;
using FDK;
using SSTFormat.v4;
using DTXMania.設定;
using DTXMania.データベース.ユーザ;
using DTXMania.入力;

namespace DTXMania.ステージ.演奏
{
    class 演奏ステージ : ステージ
    {
        public const float ヒット判定位置Ydpx = 847f;


        public enum フェーズ
        {
            フェードイン,
            表示,
            クリア,
            キャンセル通知,    // 高速進行スレッドから設定
            キャンセル時フェードアウト,
            キャンセル完了,
        }
        public フェーズ 現在のフェーズ { get; protected set; }

        /// <summary>
        ///     フェードインアイキャッチの遷移元画面。
        ///     活性化前に、外部から設定される。
        /// </summary>
        public Bitmap キャプチャ画面 { get; set; } = null;

        public 成績 成績 { get; protected set; } = null;


        public 演奏ステージ()
        {
            using( Log.Block( FDKUtilities.現在のメソッド名 ) )
            {
                this.子Activityを追加する( this._背景画像 = new 画像( @"$(System)images\演奏\演奏画面.png" ) );
                this.子Activityを追加する( this._レーンフレーム = new レーンフレーム() );
                this.子Activityを追加する( this._曲名パネル = new 曲名パネル() );
                this.子Activityを追加する( this._ドラムパッド = new ドラムパッド() );
                this.子Activityを追加する( this._ヒットバー = new ヒットバー() );
                this.子Activityを追加する( this._レーンフラッシュ = new レーンフラッシュ() );
                this.子Activityを追加する( this._ドラムチップ画像 = new 画像( @"$(System)images\演奏\ドラムチップ.png" ) );
                this.子Activityを追加する( this._判定文字列 = new 判定文字列() );
                this.子Activityを追加する( this._チップ光 = new チップ光() );
                this.子Activityを追加する( this._左サイドクリアパネル = new 左サイドクリアパネル() );
                this.子Activityを追加する( this._右サイドクリアパネル = new 右サイドクリアパネル() );
                this.子Activityを追加する( this._判定パラメータ表示 = new 判定パラメータ表示() );
                this.子Activityを追加する( this._フェーズパネル = new フェーズパネル() );
                this.子Activityを追加する( this._コンボ表示 = new コンボ表示() );
                this.子Activityを追加する( this._カウントマップライン = new カウントマップライン() );
                this.子Activityを追加する( this._スコア表示 = new スコア表示() );
                this.子Activityを追加する( this._プレイヤー名表示 = new プレイヤー名表示() );
                this.子Activityを追加する( this._譜面スクロール速度 = new 譜面スクロール速度() );
                this.子Activityを追加する( this._達成率表示 = new 達成率表示() );
                this.子Activityを追加する( this._曲別SKILL = new 曲別SKILL() );
                this.子Activityを追加する( this._エキサイトゲージ = new エキサイトゲージ() );
                this.子Activityを追加する( this._FPS = new FPS() );
                this.子Activityを追加する( this._数字フォント中グレー48x64 = new 画像フォント(
                   @"$(System)images\数字フォント中ホワイト48x64.png",
                   @"$(System)images\数字フォント中48x64矩形リスト.yaml",
                   文字幅補正dpx: -16f,
                   不透明度: 0.3f ) );
            }
        }

        protected override void On活性化()
        {
            using( Log.Block( FDKUtilities.現在のメソッド名 ) )
            {
                {
                    var 設定ファイルパス = new VariablePath( @"$(System)images\演奏\ドラムチップ.yaml" );

                    var yaml = File.ReadAllText( 設定ファイルパス.変数なしパス );
                    var deserializer = new YamlDotNet.Serialization.Deserializer();
                    var yamlMap = deserializer.Deserialize<YAMLマップ_ドラムチップ>( yaml );

                    this._ドラムチップの縦方向中央位置 = yamlMap.縦方向中央位置;
                    this._ドラムチップの矩形リスト = new Dictionary<string, RectangleF>();
                    foreach( var kvp in yamlMap.矩形リスト )
                    {
                        if( 4 == kvp.Value.Length )
                            this._ドラムチップの矩形リスト[ kvp.Key ] = new RectangleF( kvp.Value[ 0 ], kvp.Value[ 1 ], kvp.Value[ 2 ], kvp.Value[ 3 ] );
                    }
                }

                this._小節線色 = new SolidColorBrush( グラフィックデバイス.Instance.D2DDeviceContext, Color.White );
                this._拍線色 = new SolidColorBrush( グラフィックデバイス.Instance.D2DDeviceContext, Color.LightGray );
                this._ドラムチップアニメ = new LoopCounter( 0, 200, 3 );
                this._プレイヤー名表示.名前 = App.ユーザ管理.ログオン中のユーザ.ユーザ名;
                レーンフレーム.レーン配置を設定する( App.ユーザ管理.ログオン中のユーザ.レーン配置 );
                this._フェードインカウンタ = new Counter( 0, 100, 10 );

                this._演奏状態を初期化する();

                this.現在のフェーズ = フェーズ.フェードイン;
            }
        }

        protected override void On非活性化()
        {
            using( Log.Block( FDKUtilities.現在のメソッド名 ) )
            {
                this._演奏状態を終了する();

                #region " 現在の譜面スクロール速度をDBに保存。"
                //----------------
                using( var userdb = new UserDB() )
                {
                    var user = userdb.Users.Where( ( r ) => ( r.Id == App.ユーザ管理.ログオン中のユーザ.ユーザID ) ).SingleOrDefault();
                    if( null != user )
                    {
                        user.ScrollSpeed = App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度;
                        userdb.DataContext.SubmitChanges();
                        Log.Info( $"現在の譜面スクロール速度({App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度})をDBに保存しました。[{user}]" );
                    }
                }
                //----------------
                #endregion

                this._拍線色?.Dispose();
                this._拍線色 = null;

                this._小節線色?.Dispose();
                this._小節線色 = null;

                this.キャプチャ画面?.Dispose();
                this.キャプチャ画面 = null;
            }
        }

        /// <summary>
        ///     <see cref="App.演奏スコア"/> に対して、ステージを初期化する。
        /// </summary>
        private void _演奏状態を初期化する()
        {
            // スコアに依存するデータを初期化する。

            this.成績 = new 成績();
            this.成績.スコアと設定を反映する( App.演奏スコア, App.ユーザ管理.ログオン中のユーザ );

            this._カウントマップライン.非活性化する();
            this._カウントマップライン.活性化する();

            this._描画開始チップ番号 = -1;

            this._チップの演奏状態 = new Dictionary<チップ, チップの演奏状態>();
            foreach( var chip in App.演奏スコア.チップリスト )
                this._チップの演奏状態.Add( chip, new チップの演奏状態( chip ) );


            // WAVを生成する。

            App.WAVキャッシュレンタル.世代を進める();

            App.WAV管理?.Dispose();
            App.WAV管理 = new 曲.WAV管理();

            foreach( var kvp in App.演奏スコア.WAVリスト )
            {
                var path = Path.Combine( App.演奏スコア.PATH_WAV, kvp.Value.ファイルパス );
                App.WAV管理.登録する( App.サウンドデバイス, kvp.Key, path, kvp.Value.多重再生する );
            }


            // AVIを生成する。

            App.AVI管理?.Dispose();
            App.AVI管理 = new 曲.AVI管理();

            foreach( var kvp in App.演奏スコア.AVIリスト )
            {
                var path = Path.Combine( App.演奏スコア.PATH_WAV, kvp.Value );
                App.AVI管理.登録する( kvp.Key, path );
            }

            this._初めての進行描画 = true;
        }

        private void _演奏状態を終了する()
        {
            this._描画開始チップ番号 = -1;

            //App.WAV管理?.Dispose();	
            //App.WAV管理 = null;
            App.AVI管理?.Dispose();
            App.AVI管理 = null;
        }

        public override void 高速進行する()
        {
            try
            {
                if( this._初めての進行描画 )
                {
                    this._初めての進行描画 = false;
                }

                // 高速進行

                this._FPS.FPSをカウントしプロパティを更新する();

                switch( this.現在のフェーズ )
                {
                    case フェーズ.フェードイン:
                        if( this._フェードインカウンタ.終了値に達した )
                        {
                            #region " フェードインが終わったので、演奏開始。 "
                            //----------------
                            Log.Info( "演奏を開始します。" );

                            this._描画開始チップ番号 = 0; // -1 から 0 に変われば演奏開始。

                            App.サウンドタイマ.リセットする();

                            this.現在のフェーズ = フェーズ.表示;

                            // ここで break; すると、次の表示フェーズまで１フレーム分の時間（数ミリ秒）が空いてしまう。
                            // なので、フレームが空かないように、ここですぐさま最初の表示フェーズを実行する。
                            this.高速進行する();
                            //----------------
                            #endregion
                        }
                        break;

                    case フェーズ.表示:

                        // ※注:クリアや失敗の判定は、ここではなく、進行描画側で行っている。

                        double 現在の演奏時刻sec = this._演奏開始からの経過時間secを返す();


                        // AutoPlay 判定

                        #region " 自動ヒット処理。"
                        //----------------
                        this._描画範囲内のすべてのチップに対して( 現在の演奏時刻sec, ( chip, index, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離dpx ) => {

                            var ユーザ設定 = App.ユーザ管理.ログオン中のユーザ;
                            var ドラムチッププロパティ = ユーザ設定.ドラムチッププロパティ管理[ chip.チップ種別 ];
                            var AutoPlay = ユーザ設定.AutoPlay[ ドラムチッププロパティ.AutoPlay種別 ];

                            bool チップはヒット済みである = this._チップの演奏状態[ chip ].ヒット済みである;
                            bool チップはまだヒットされていない = !( チップはヒット済みである );
                            bool チップはMISSエリアに達している = ( ヒット判定バーと描画との時間sec > ユーザ設定.最大ヒット距離sec[ 判定種別.OK ] );
                            bool チップは描画についてヒット判定バーを通過した = ( 0 <= ヒット判定バーと描画との時間sec );
                            bool チップは発声についてヒット判定バーを通過した = ( 0 <= ヒット判定バーと発声との時間sec );

                            if( チップはまだヒットされていない && チップはMISSエリアに達している )
                            {
                                #region " MISS判定。"
                                //----------------
                                if( AutoPlay && ドラムチッププロパティ.AutoPlayON_Miss判定 )
                                {
                                    this._チップのヒット処理を行う(
                                        chip,
                                        判定種別.MISS,
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_再生,
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_判定,
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_非表示,
                                        ヒット判定バーと発声との時間sec );
                                    return;
                                }
                                else if( !AutoPlay && ドラムチッププロパティ.AutoPlayOFF_Miss判定 )
                                {
                                    this._チップのヒット処理を行う(
                                        chip,
                                        判定種別.MISS,
                                        ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_再生,
                                        ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_判定,
                                        ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_非表示,
                                        ヒット判定バーと発声との時間sec );

                                    this.成績.エキサイトゲージを加算する( 判定種別.MISS ); // 手動演奏なら MISS はエキサイトゲージに反映。
                                    return;
                                }
                                else
                                {
                                    // 通過。
                                }
                                //----------------
                                #endregion
                            }

                            // ヒット処理(1) 発声時刻
                            if( チップは発声についてヒット判定バーを通過した )    // ヒット済みかどうかには関係ない
                            {
                                #region " 自動ヒット判定。"
                                //----------------
                                if( ( AutoPlay && ドラムチッププロパティ.AutoPlayON_自動ヒット_再生 ) ||
                                    ( !AutoPlay && ドラムチッププロパティ.AutoPlayOFF_自動ヒット_再生 ) )
                                {
                                    // チップの発声がまだなら発声を行う。

                                    if( !( this._チップの演奏状態[ chip ].発声済みである ) )
                                    {
                                        this._チップの発声を行う( chip, ヒット判定バーと発声との時間sec );
                                        this._チップの演奏状態[ chip ].発声済みである = true;
                                    }

                                }
                                //----------------
                                #endregion
                            }

                            // ヒット処理(2) 描画時刻
                            if( チップはまだヒットされていない && チップは描画についてヒット判定バーを通過した )
                            {
                                #region " 自動ヒット判定。"
                                //----------------
                                if( AutoPlay && ドラムチッププロパティ.AutoPlayON_自動ヒット )
                                {
                                    this._チップのヒット処理を行う(
                                        chip,
                                        判定種別.PERFECT,   // AutoPlay 時は Perfect 扱い。
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_再生,
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_判定,
                                        ドラムチッププロパティ.AutoPlayON_自動ヒット_非表示,
                                        ヒット判定バーと発声との時間sec );

                                    //this.成績.エキサイトゲージを加算する( 判定種別.PERFECT ); -> エキサイトゲージには反映しない。
                                    return;
                                }
                                else if( !AutoPlay && ドラムチッププロパティ.AutoPlayOFF_自動ヒット )
                                {
                                    this._チップのヒット処理を行う(
                                        chip,
                                        判定種別.PERFECT,   // AutoPlay OFF でも自動ヒットする場合は Perfect 扱い。
                                        ドラムチッププロパティ.AutoPlayOFF_自動ヒット_再生,
                                        ドラムチッププロパティ.AutoPlayOFF_自動ヒット_判定,
                                        ドラムチッププロパティ.AutoPlayOFF_自動ヒット_非表示,
                                        ヒット判定バーと発声との時間sec );

                                    //this.成績.エキサイトゲージを加算する( 判定種別.PERFECT ); -> エキサイトゲージには反映しない。
                                    return;
                                }
                                else
                                {
                                    // 通過。
                                }
                                //----------------
                                #endregion
                            }

                        } );
                        //----------------
                        #endregion


                        // 入力(1) 手動演奏

                        App.入力管理.すべての入力デバイスをポーリングする( 入力履歴を記録する: false );

                        #region " ユーザヒット処理。"
                        //----------------
                        {
                            var ヒット処理済み入力 = new List<ドラム入力イベント>(); // ヒット処理が終わった入力は、二重処理しないよう、この中に追加しておく。

                            var ユーザ設定 = App.ユーザ管理.ログオン中のユーザ;

                            #region " 描画範囲内のすべてのチップについて、対応する入力があればヒット処理を行う。"
                            //----------------
                            this._描画範囲内のすべてのチップに対して( 現在の演奏時刻sec, ( chip, index, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離 ) => {

                                // チップにヒットしている入力を探す。

                                var ドラムチッププロパティ = ユーザ設定.ドラムチッププロパティ管理[ chip.チップ種別 ];
                                var AutoPlayである = ユーザ設定.AutoPlay[ ドラムチッププロパティ.AutoPlay種別 ];

                                bool チップはヒット済みである = this._チップの演奏状態[ chip ].ヒット済みである;
                                bool チップはMISSエリアに達している = ( ヒット判定バーと描画との時間sec > ユーザ設定.最大ヒット距離sec[ 判定種別.OK ] );
                                bool チップは描画についてヒット判定バーを通過した = ( 0 <= ヒット判定バーと描画との時間sec );
                                bool チップは発声についてヒット判定バーを通過した = ( 0 <= ヒット判定バーと発声との時間sec );

                                if( チップはヒット済みである || // ヒット済みなら何もしない。
                                    AutoPlayである ||          // AutoPlay チップなので何もしない。
                                    !( ドラムチッププロパティ.AutoPlayOFF_ユーザヒット ) ||   // このチップは AutoPlay OFF の時でもユーザヒットの対象ではないので何もしない。
                                    !( ヒット判定バーと描画との時間sec >= -( ユーザ設定.最大ヒット距離sec[ 判定種別.OK ] ) && !( チップはMISSエリアに達している ) ) )    // チップはヒット可能エリアの外にあるので何もしない。
                                    return;

                                var チップにヒットしている入力 = App.入力管理.ポーリング結果.FirstOrDefault( ( 入力 ) => {

                                    if( 入力.InputEvent.離された ||                   // 押下入力じゃないなら無視。
                                        ヒット処理済み入力.Contains( 入力 ) ||         // すでに今回のターンで処理済み（＝処理済み入力リストに追加済み）なら無視。
                                        入力.Type == ドラム入力種別.HiHat_Control )    // HiHat_Control 入力はここでは無視。
                                        return false;

                                    var チップの入力グループ = ドラムチッププロパティ.入力グループ種別;

                                    // (A) 入力グループ種別 が Unknown の場合 → ドラム入力種別で比較
                                    if( チップの入力グループ == 入力グループ種別.Unknown )
                                    {
                                        return ( ドラムチッププロパティ.ドラム入力種別 == 入力.Type );
                                    }
                                    // (B) 入力グループ種別が Unknown ではない場合　→　入力グループ種別で比較
                                    else
                                    {
                                        var 入力の入力グループ = ユーザ設定.ドラムチッププロパティ管理.チップtoプロパティ.First( ( kvp ) => ( kvp.Value.ドラム入力種別 == 入力.Type ) ).Value.入力グループ種別;

                                        return ( チップの入力グループ == 入力の入力グループ );
                                    }

                                } );

                                // チップにヒットした入力があった。

                                if( null != チップにヒットしている入力 )
                                {
                                    ヒット処理済み入力.Add( チップにヒットしている入力 );    // この入力はこのチップでヒット処理した。

                                    // 判定を算出。
                                    var 判定 = 判定種別.OK;
                                    double ヒット判定バーとの時間の絶対値sec = Math.Abs( ヒット判定バーと描画との時間sec );
                                    switch( ヒット判定バーとの時間の絶対値sec )
                                    {
                                        case double span when( span <= ユーザ設定.最大ヒット距離sec[ 判定種別.PERFECT ] ): 判定 = 判定種別.PERFECT; break;
                                        case double span when( span <= ユーザ設定.最大ヒット距離sec[ 判定種別.GREAT ] ): 判定 = 判定種別.GREAT; break;
                                        case double span when( span <= ユーザ設定.最大ヒット距離sec[ 判定種別.GOOD ] ): 判定 = 判定種別.GOOD; break;
                                        default: 判定 = 判定種別.OK; break;
                                    }

                                    // ヒット処理。
                                    this._チップのヒット処理を行う(
                                        chip,
                                        判定,
                                        ( ユーザ設定.ドラムの音を発声する ) ? ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_再生 : false,
                                        ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_判定,
                                        ドラムチッププロパティ.AutoPlayOFF_ユーザヒット_非表示,
                                        ヒット判定バーと発声との時間sec );

                                    // エキサイトゲージに反映する。
                                    this.成績.エキサイトゲージを加算する( 判定 );
                                }

                            } );
                            //----------------
                            #endregion

                            #region " ヒットしてようがしてまいが起こすアクションを実行。"
                            //----------------
                            {
                                var アクション済み入力 = new List<ドラム入力イベント>();  // ヒット処理が終わった入力は、二重処理しないよう、この中に追加しておく。

                                foreach( var 入力 in App.入力管理.ポーリング結果 )
                                {
                                    // 押下入力じゃないなら無視。
                                    if( 入力.InputEvent.離された )
                                        continue;

                                    var プロパティs = ユーザ設定.ドラムチッププロパティ管理.チップtoプロパティ.Where( ( kvp ) => ( kvp.Value.ドラム入力種別 == 入力.Type ) );

                                    if( プロパティs.Count() > 0 )
                                    {
                                        //for( int i = 0; i < プロパティs.Count(); i++ )
                                        int i = 0;  // １つの入力で処理するのは、１つの表示レーン種別のみ。
                                        {
                                            this._ドラムパッド.ヒットする( プロパティs.ElementAt( i ).Value.表示レーン種別 );
                                            this._レーンフラッシュ.開始する( プロパティs.ElementAt( i ).Value.表示レーン種別 );
                                        }
                                    }
                                }
                            }
                            //----------------
                            #endregion

                            #region " どのチップにもヒットしなかった入力は空打ちとみなし、空打ち音を再生する。"
                            //----------------
                            if( ユーザ設定.ドラムの音を発声する )
                            {
                                foreach( var 入力 in App.入力管理.ポーリング結果 )
                                {
                                    if( ヒット処理済み入力.Contains( 入力 ) ||   // ヒット済みなら無視。
                                        入力.InputEvent.離された )              // 押下じゃないなら無視。
                                        continue;

                                    var プロパティs = ユーザ設定.ドラムチッププロパティ管理.チップtoプロパティ.Where( ( kvp ) => ( kvp.Value.ドラム入力種別 == 入力.Type ) );

                                    for( int i = 0; i < プロパティs.Count(); i++ )
                                    {
                                        var prop = プロパティs.ElementAt( i ).Value;

                                        if( 0 < App.演奏スコア.空打ちチップマップ.Count )
                                        {
                                            #region " DTX他の場合（空うちチップマップ使用）"
                                            //----------------
                                            int zz = App.演奏スコア.空打ちチップマップ[ prop.レーン種別 ];

                                            // (A) 空打ちチップの指定があるなら、それを発声する。
                                            if( 0 != zz )
                                                App.WAV管理.発声する( zz, prop.チップ種別, prop.発声前消音, prop.消音グループ種別 );

                                            // (B) 空打ちチップの指定がないなら、一番近いチップを検索し、それを発声する。
                                            else
                                            {
                                                var chip = this._指定された時刻に一番近いチップを返す( 現在の演奏時刻sec, 入力.Type );

                                                if( null != chip )
                                                    this._チップの発声を行う( chip, 現在の演奏時刻sec );
                                            }
                                            //----------------
                                            #endregion
                                        }
                                        else
                                        {
                                            #region " SSTFの場合（空うちチップマップ未使用）"
                                            //----------------
                                            App.ドラムサウンド.発声する( prop.チップ種別, 0, prop.発声前消音, prop.消音グループ種別 );
                                            //----------------
                                            #endregion
                                        }
                                    }
                                }
                            }
                            //----------------
                            #endregion

                            ヒット処理済み入力 = null;
                        }
                        //----------------
                        #endregion


                        // 入力(2) 演奏以外の操作（※演奏中なのでドラム入力は無視。）

                        if( App.入力管理.Keyboard.キーが押された( 0, Key.Escape ) )
                        {
                            #region " ESC → 演奏中断 "
                            //----------------
                            Log.Info( "ESC キーが押されました。演奏を中断します。" );

                            this.BGMを停止する();
                            App.WAV管理.すべての発声を停止する();    // DTXでのBGMサウンドはこっちに含まれる。

                            // 進行描画スレッドへの通知フェーズを挟む。
                            this.現在のフェーズ = フェーズ.キャンセル通知;
                            //----------------
                            #endregion
                        }
                        if( App.入力管理.Keyboard.キーが押された( 0, Key.Up ) )
                        {
                            #region " 上 → 譜面スクロールを加速 "
                            //----------------
                            const double 最大倍率 = 8.0;
                            App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 = Math.Min( App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 + 0.5, 最大倍率 );
                            //----------------
                            #endregion
                        }
                        if( App.入力管理.Keyboard.キーが押された( 0, Key.Down ) )
                        {
                            #region " 下 → 譜面スクロールを減速 "
                            //----------------
                            const double 最小倍率 = 0.5;
                            App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 = Math.Max( App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 - 0.5, 最小倍率 );
                            //----------------
                            #endregion
                        }
                        break;

                    case フェーズ.キャンセル完了:
                    case フェーズ.キャンセル時フェードアウト:
                    case フェーズ.キャンセル通知:
                    case フェーズ.クリア:
                        break;
                }
            }
            catch( Exception e )
            {
                Log.ERROR( $"!!! 高速進行スレッドで例外が発生しました !!! [{Folder.絶対パスをフォルダ変数付き絶対パスに変換して返す( e.Message )}]" );
            }
        }

        public override void 進行描画する( DeviceContext1 dc )
        {
            // 進行描画

            if( this._初めての進行描画 )
                return; // まだ最初の高速進行が行われていない。

            switch( this.現在のフェーズ )
            {
                case フェーズ.フェードイン:
                case フェーズ.キャンセル完了:
                    {
                        this._左サイドクリアパネル.クリアする();
                        this._左サイドクリアパネル.クリアパネル.テクスチャへ描画する( ( dcp ) => {
                            this._プレイヤー名表示.進行描画する( dcp );
                            this._スコア表示.進行描画する( dcp, グラフィックデバイス.Instance.Animation, new Vector2( +280f, +120f ), this.成績 );
                            this._達成率表示.描画する( dcp, (float) this.成績.Achievement );
                            this._判定パラメータ表示.描画する( dcp, +118f, +372f, this.成績 );
                            this._曲別SKILL.進行描画する( dcp, 0f );
                        } );
                        this._左サイドクリアパネル.描画する( dc );

                        this._右サイドクリアパネル.クリアする();
                        this._右サイドクリアパネル.描画する( dc );

                        this._レーンフレーム.描画する( dc, App.ユーザ管理.ログオン中のユーザ.レーンの透明度 );
                        this._ドラムパッド.進行描画する( dc );
                        this._背景画像.描画する( dc, 0f, 0f );
                        this._譜面スクロール速度.描画する( dc, App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 );
                        this._エキサイトゲージ.進行描画する( dc, this.成績.エキサイトゲージ量 );

                        this._カウントマップライン.進行描画する( dc );
                        this._フェーズパネル.進行描画する( dc );
                        this._曲名パネル.描画する( dc );
                        this._ヒットバー.描画する( dc );
                        this._キャプチャ画面を描画する( dc, ( 1.0f - this._フェードインカウンタ.現在値の割合 ) );
                    }
                    break;

                case フェーズ.クリア:
                    this._背景画像.描画する( dc, 0f, 0f );
                    break;

                case フェーズ.表示:
                case フェーズ.キャンセル時フェードアウト:
                    {
                        double 演奏時刻sec = this._演奏開始からの経過時間secを返す() + グラフィックデバイス.Instance.次のDComp表示までの残り時間sec;

                        this._譜面スクロール速度.進行する( App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 );  // チップの表示より前に進行だけ行う

                        #region " AVI（動画）の進行描画を行う。"
                        //----------------
                        foreach( var kvp in App.AVI管理.動画リスト )
                        {
                            int zz = kvp.Key;
                            var video = kvp.Value;

                            if( video.再生中 )
                            {
                                // (A) 75%縮小表示
                                {
                                    float w = グラフィックデバイス.Instance.設計画面サイズ.Width;
                                    float h = グラフィックデバイス.Instance.設計画面サイズ.Height;

                                    // (1) 画面いっぱいに描画。
                                    video.描画する( dc, new RectangleF( 0f, 0f, w, h ), 0.2f );    // 不透明度は 0.2 で暗くする。

                                    float 拡大縮小率 = 0.75f;
                                    float 上移動 = 100.0f;

                                    // (2) ちょっと縮小して描画。
                                    video.最後のフレームを再描画する( dc, new RectangleF(   // 直前に取得したフレームをそのまま描画。
                                        w * ( 1f - 拡大縮小率 ) / 2f,
                                        h * ( 1f - 拡大縮小率 ) / 2f - 上移動,
                                        w * 拡大縮小率,
                                        h * 拡大縮小率 ) );
                                }
                                // (B) 100%全体表示のみ --> 今は未対応
                                {
                                    //float w = グラフィックデバイス.Instance.設計画面サイズ.Width;
                                    //float h = グラフィックデバイス.Instance.設計画面サイズ.Height;
                                    //video.描画する( dc, new RectangleF( 0f, 0f, w, h ), 0.2f );    // 不透明度は 0.2 で暗くする。
                                }
                            }
                        }
                        //----------------
                        #endregion

                        this._左サイドクリアパネル.クリアする();
                        this._左サイドクリアパネル.クリアパネル.テクスチャへ描画する( ( dcp ) => {
                            this._プレイヤー名表示.進行描画する( dcp );
                            this._スコア表示.進行描画する( dcp, グラフィックデバイス.Instance.Animation, new Vector2( +280f, +120f ), this.成績 );
                            this._達成率表示.描画する( dcp, (float) this.成績.Achievement );
                            this._判定パラメータ表示.描画する( dcp, +118f, +372f, this.成績 );
                            this._曲別SKILL.進行描画する( dcp, this.成績.Skill );
                        } );
                        this._左サイドクリアパネル.描画する( dc );

                        this._右サイドクリアパネル.クリアする();
                        this._右サイドクリアパネル.クリアパネル.テクスチャへ描画する( ( dcp ) => {
                            this._コンボ表示.進行描画する( dcp, グラフィックデバイス.Instance.Animation, new Vector2( +228f + 264f / 2f, +234f ), this.成績 );
                        } );
                        this._右サイドクリアパネル.描画する( dc );

                        this._レーンフレーム.描画する( dc, App.ユーザ管理.ログオン中のユーザ.レーンの透明度 );
                        this._レーンフラッシュ.進行描画する( dc );
                        this._小節線拍線を描画する( dc, 演奏時刻sec );
                        this._ドラムパッド.進行描画する( dc );
                        this._背景画像.描画する( dc, 0f, 0f );
                        this._譜面スクロール速度.描画する( dc, App.ユーザ管理.ログオン中のユーザ.譜面スクロール速度 );
                        this._エキサイトゲージ.進行描画する( dc, this.成績.エキサイトゲージ量 );

                        double 曲の長さsec = App.演奏スコア.チップリスト[ App.演奏スコア.チップリスト.Count - 1 ].描画時刻sec;
                        float 現在位置 = (float) ( 1.0 - ( 曲の長さsec - 演奏時刻sec ) / 曲の長さsec );
                        this._カウントマップライン.カウント値を設定する( 現在位置, this.成績.判定toヒット数 );
                        this._カウントマップライン.進行描画する( dc );
                        this._フェーズパネル.現在位置 = 現在位置;
                        this._フェーズパネル.進行描画する( dc );
                        this._曲名パネル.描画する( dc );
                        this._ヒットバー.描画する( dc );
                        this._チップを描画する( dc, 演奏時刻sec );  // クリア判定はこの中。
                        this._チップ光.進行描画する( dc );
                        this._判定文字列.進行描画する( dc );

                        this._FPS.VPSをカウントする();
                        this._FPS.描画する( dc, 0f, 0f );

                        if( this.現在のフェーズ == フェーズ.キャンセル時フェードアウト )
                        {
                            App.ステージ管理.現在のアイキャッチ.進行描画する( dc );

                            if( App.ステージ管理.現在のアイキャッチ.現在のフェーズ == アイキャッチ.アイキャッチ.フェーズ.クローズ完了 )
                            {
                                this.現在のフェーズ = フェーズ.キャンセル完了;
                            }
                        }
                    }
                    break;

                case フェーズ.キャンセル通知:
                    App.ステージ管理.アイキャッチを選択しクローズする( nameof( アイキャッチ.半回転黒フェード ) );
                    this.現在のフェーズ = フェーズ.キャンセル時フェードアウト;
                    break;
            }

        }

        /// <remarks>
        ///		演奏クリア時には、次の結果ステージに入ってもBGMが鳴り続ける。
        ///		そのため、後からBGMだけを別個に停止するためのメソッドが必要になる。
        /// </remarks>
        public void BGMを停止する()
        {
            using( Log.Block( FDKUtilities.現在のメソッド名 ) )
            {
                App.WAV管理.すべての発声を停止する();
            }
        }


        // 画面を構成するもの

        private 画像 _背景画像 = null;
        private 曲名パネル _曲名パネル = null;
        private FPS _FPS = null;
        private レーンフレーム _レーンフレーム = null;
        private ヒットバー _ヒットバー = null;
        private ドラムパッド _ドラムパッド = null;
        private 譜面スクロール速度 _譜面スクロール速度 = null;
        private エキサイトゲージ _エキサイトゲージ = null;
        private フェーズパネル _フェーズパネル = null;
        private カウントマップライン _カウントマップライン = null;
        private 左サイドクリアパネル _左サイドクリアパネル = null;
        private 右サイドクリアパネル _右サイドクリアパネル = null;

        // 左サイドクリアパネル内に表示されるもの

        private スコア表示 _スコア表示 = null;
        private プレイヤー名表示 _プレイヤー名表示 = null;
        private 判定パラメータ表示 _判定パラメータ表示 = null;
        private 達成率表示 _達成率表示 = null;
        private 曲別SKILL _曲別SKILL = null;

        // 右サイドクリアパネル内に表示されるもの

        private コンボ表示 _コンボ表示 = null;

        // 譜面上に表示されるもの

        private レーンフラッシュ _レーンフラッシュ = null;
        private 判定文字列 _判定文字列 = null;
        private チップ光 _チップ光 = null;
        private 画像フォント _数字フォント中グレー48x64 = null;

        private SolidColorBrush _小節線色 = null;
        private SolidColorBrush _拍線色 = null;
        private void _小節線拍線を描画する( DeviceContext1 dc, double 現在の演奏時刻sec )
        {
            // 小節線・拍線 と チップ は描画階層（奥行き）が異なるので、別々のメソッドに分ける。

            グラフィックデバイス.Instance.D2DBatchDraw( dc, () => {

                this._描画範囲内のすべてのチップに対して( 現在の演奏時刻sec, ( chip, index, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離dpx ) => {

                    if( chip.チップ種別 == チップ種別.小節線 )
                    {
                        // 小節線
                        float 上位置dpx = (float) ( ヒット判定位置Ydpx + ヒット判定バーとの距離dpx - 1f );   // -1f は小節線の厚みの半分。
                        dc.DrawLine( new Vector2( 441f, 上位置dpx ), new Vector2( 441f + 780f, 上位置dpx ), this._小節線色, strokeWidth: 3f );

                        // 小節番号
                        float 右位置dpx = 441f + 780f - 24f;   // -24f は適当なマージン。
                        this._数字フォント中グレー48x64.描画する( dc, 右位置dpx, 上位置dpx - 84f, chip.小節番号.ToString(), 右揃え: true );	// -84f は適当なマージン。
                    }

                    // 拍線
                    else if( chip.チップ種別 == チップ種別.拍線 )
                    {
                        float 上位置dpx = (float) ( ヒット判定位置Ydpx + ヒット判定バーとの距離dpx - 1f );   // -1f は拍線の厚みの半分。
                        dc.DrawLine( new Vector2( 441f, 上位置dpx ), new Vector2( 441f + 780f, 上位置dpx ), this._拍線色, strokeWidth: 1f );
                    }

                } );

            } );
        }

        private 画像 _ドラムチップ画像 = null;
        private Dictionary<string, RectangleF> _ドラムチップの矩形リスト = null;
        private float _ドラムチップの縦方向中央位置 = 0f;
        private LoopCounter _ドラムチップアニメ = null;
        private void _チップを描画する( DeviceContext1 dc, double 現在の演奏時刻sec )
        {
            Debug.Assert( null != this._ドラムチップの矩形リスト );

            this._描画範囲内のすべてのチップに対して( 現在の演奏時刻sec, ( chip, index, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離dpx ) => {

                float たて中央位置dpx = (float) ( ヒット判定位置Ydpx + ヒット判定バーとの距離dpx );
                float 消滅割合 = 0f;

                #region " 消滅割合を算出; チップがヒット判定バーを通過したら徐々に消滅する。"
                //----------------
                const float 消滅を開始するヒット判定バーからの距離dpx = 20f;
                const float 消滅開始から完全消滅するまでの距離dpx = 70f;

                if( 消滅を開始するヒット判定バーからの距離dpx < ヒット判定バーとの距離dpx )   // 通過した
                {
                    // 通過距離に応じて 0→1の消滅割合を付与する。0で完全表示、1で完全消滅、通過してなければ 0。
                    消滅割合 = Math.Min( 1f, (float) ( ( ヒット判定バーとの距離dpx - 消滅を開始するヒット判定バーからの距離dpx ) / 消滅開始から完全消滅するまでの距離dpx ) );
                }
                //----------------
                #endregion

                #region " チップが描画開始チップであり、かつ、そのY座標が画面下端を超えたなら、描画開始チップ番号を更新する。"
                //----------------
                if( ( index == this._描画開始チップ番号 ) &&
                    ( グラフィックデバイス.Instance.設計画面サイズ.Height + 40.0 < たて中央位置dpx ) )   // +40 はチップが隠れるであろう適当なマージン。
                {
                    this._描画開始チップ番号++;

                    // 描画開始チップがチップリストの末尾に到達したら、演奏を終了する。
                    if( App.演奏スコア.チップリスト.Count <= this._描画開始チップ番号 )
                    {
                        this.現在のフェーズ = フェーズ.クリア;
                        this._描画開始チップ番号 = -1;    // 演奏完了。
                        return;
                    }
                }
                //----------------
                #endregion

                if( this._チップの演奏状態[ chip ].不可視 )
                    return;

                // チップの大きさを計算する。
                float 大きさ0to1 = 1.0f;
                if( App.ユーザ管理.ログオン中のユーザ.演奏モード == PlayMode.EXPERT )
                {
                    // 音量により大きさ可変。
                    大きさ0to1 = Math.Max( 0.3f, Math.Min( 1.0f, chip.音量 / (float) チップ.既定音量 ) );   // 既定音量未満は大きさを小さくするが、既定音量以上は大きさ1.0のままとする。最小は 0.3。
                    if( chip.チップ種別 == チップ種別.Snare_Ghost )   // Ghost は対象外
                        大きさ0to1 = 1.0f;
                }

                // チップ種別 から、表示レーン種別 と 表示チップ種別 を取得。
                var 表示レーン種別 = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理[ chip.チップ種別 ].表示レーン種別;
                var 表示チップ種別 = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理[ chip.チップ種別 ].表示チップ種別;

                if( ( 表示レーン種別 != 表示レーン種別.Unknown ) &&   // Unknwon ならチップを表示しない。
                    ( 表示チップ種別 != 表示チップ種別.Unknown ) )    //
                {
                    var たて方向中央位置dpx = this._ドラムチップの縦方向中央位置;
                    var 左端位置dpx = レーンフレーム.領域.Left + レーンフレーム.現在のレーン配置.表示レーンの左端位置dpx[ 表示レーン種別 ];
                    var 中央位置Xdpx = 左端位置dpx + レーンフレーム.現在のレーン配置.表示レーンの幅dpx[ 表示レーン種別 ] / 2f;

                    #region " チップ背景（あれば）を描画する。"
                    //----------------
                    {
                        var 矩形 = this._ドラムチップの矩形リスト[ 表示チップ種別.ToString() + "_back" ];

                        if( ( null != 矩形 ) && ( ( 0 < 矩形.Width && 0 < 矩形.Height ) ) )
                        {
                            var 矩形中央 = new Vector2( 矩形.Width / 2f, 矩形.Height / 2f );
                            var アニメ割合 = this._ドラムチップアニメ.現在値の割合;   // 0→1のループ

                            var 変換行列2D = ( 0 >= 消滅割合 ) ? Matrix3x2.Identity : Matrix3x2.Scaling( 1f - 消滅割合, 1f, 矩形中央 );

                            // 変換(1) 拡大縮小、回転
                            // → 現在は、どの表示チップ種別の背景がどのアニメーションを行うかは、コード内で名指しする（固定）。
                            switch( 表示チップ種別 )
                            {
                                case 表示チップ種別.LeftCymbal:
                                case 表示チップ種別.RightCymbal:
                                case 表示チップ種別.HiHat:
                                case 表示チップ種別.HiHat_Open:
                                case 表示チップ種別.HiHat_HalfOpen:
                                case 表示チップ種別.Foot:
                                case 表示チップ種別.LeftPedal:
                                case 表示チップ種別.LeftBass:
                                case 表示チップ種別.Tom3:
                                case 表示チップ種別.Tom3_Rim:
                                case 表示チップ種別.LeftRide:
                                case 表示チップ種別.RightRide:
                                case 表示チップ種別.LeftRide_Cup:
                                case 表示チップ種別.RightRide_Cup:
                                case 表示チップ種別.LeftChina:
                                case 表示チップ種別.RightChina:
                                case 表示チップ種別.LeftSplash:
                                case 表示チップ種別.RightSplash:
                                    #region " 縦横に伸び縮み "
                                    //----------------
                                    {
                                        float v = (float) ( Math.Sin( 2 * Math.PI * アニメ割合 ) * 0.2 );    // -0.2～0.2 の振動

                                        //変換行列2D = 変換行列2D * Matrix3x2.Scaling( (float) ( 1 + v ), (float) ( 1 - v ) * 大きさ0to1, 矩形中央 );
                                        変換行列2D = 変換行列2D * Matrix3x2.Scaling( (float) ( 1 + v ), (float) ( 1 - v ) * 1.0f, 矩形中央 );       // チップ背景は大きさを変えない
                                    }
                                    //----------------
                                    #endregion
                                    break;

                                case 表示チップ種別.Bass:
                                    #region " 左右にゆらゆら回転 "
                                    //----------------
                                    {
                                        float r = (float) ( Math.Sin( 2 * Math.PI * アニメ割合 ) * 0.2 );    // -0.2～0.2 の振動
                                        変換行列2D = 変換行列2D *
                                            //Matrix3x2.Scaling( 1f, 大きさ0to1, 矩形中央 ) *
                                            Matrix3x2.Scaling( 1f, 1f, 矩形中央 ) * // チップ背景は大きさを変えない
                                            Matrix3x2.Rotation( (float) ( r * Math.PI ), 矩形中央 );
                                    }
                                    //----------------
                                    #endregion
                                    break;
                            }

                            // 変換(2) 移動
                            変換行列2D = 変換行列2D *
                                //Matrix3x2.Translation( 左端位置dpx, ( たて中央位置dpx - たて方向中央位置dpx * 大きさ0to1 ) );
                                Matrix3x2.Translation( 左端位置dpx, ( たて中央位置dpx - たて方向中央位置dpx * 1.0f ) );       // チップ背景は大きさを変えない

                            // 描画。
                            if( 表示チップ種別 != 表示チップ種別.HiHat &&         // 暫定処置：これらでは背景画像を表示しない 
                                表示チップ種別 != 表示チップ種別.LeftRide &&      //
                                表示チップ種別 != 表示チップ種別.RightRide &&     //
                                表示チップ種別 != 表示チップ種別.LeftRide_Cup &&  // 
                                表示チップ種別 != 表示チップ種別.RightRide_Cup )
                            {
                                this._ドラムチップ画像.描画する(
                                    dc,
                                    変換行列2D,
                                    転送元矩形: 矩形 );
                            }
                        }
                    }
                    //----------------
                    #endregion

                    #region " チップ本体を描画する。"
                    //----------------
                    {
                        var 矩形 = this._ドラムチップの矩形リスト[ 表示チップ種別.ToString() ];

                        if( ( null != 矩形 ) && ( ( 0 < 矩形.Width && 0 < 矩形.Height ) ) )
                        {
                            var 矩形中央 = new Vector2( 矩形.Width / 2f, 矩形.Height / 2f );

                            // 変換。
                            var 変換行列2D =
                                ( ( 0 >= 消滅割合 ) ? Matrix3x2.Identity : Matrix3x2.Scaling( 1f - 消滅割合, 1f, 矩形中央 ) ) *
                                Matrix3x2.Scaling( 0.6f + ( 0.4f * 大きさ0to1 ), 大きさ0to1, 矩形中央 ) *     // 大きさ: 0→1 のとき、幅 x0.6→x1.0
                                Matrix3x2.Translation( 左端位置dpx, ( たて中央位置dpx - たて方向中央位置dpx ) );

                            // スネアとタムのみ不透明度を反映。
                            float 不透明度 = ( chip.チップ種別 == チップ種別.Snare || chip.チップ種別 == チップ種別.Tom1 || chip.チップ種別 == チップ種別.Tom2 || chip.チップ種別 == チップ種別.Tom3 ) ?
                                ( 0.4f + ( 0.6f * 大きさ0to1 ) ) : 1f;

                            // 描画。
                            this._ドラムチップ画像.描画する(
                                dc,
                                変換行列2D,
                                転送元矩形: 矩形 );
                        }
                    }
                    //----------------
                    #endregion
                }

            } );
        }

        // 演奏状態

        /// <summary>
        ///		<see cref="スコア表示.チップリスト"/> のうち、描画を始めるチップのインデックス番号。
        ///		未演奏時・演奏終了時は -1 。
        /// </summary>
        /// <remarks>
        ///		演奏開始直後は 0 で始まり、対象番号のチップが描画範囲を流れ去るたびに +1 される。
        ///		このメンバの更新は、高頻度進行タスクではなく、進行描画メソッドで行う。（低精度で構わないので）
        /// </remarks>
        private int _描画開始チップ番号 = -1;

        private Dictionary<チップ, チップの演奏状態> _チップの演奏状態 = null;

        private double _演奏開始からの経過時間secを返す()
            => App.サウンドタイマ.現在時刻sec;


        // ステージ切り替え（特別にアイキャッチを使わないパターン）

        /// <summary>
        ///		読み込み画面: 0 ～ 1: 演奏画面
        /// </summary>
        private Counter _フェードインカウンタ = null;


        /// <summary>
        ///		<see cref="_描画開始チップ番号"/> から画面上端にはみ出すまでの間の各チップに対して、指定された処理を適用する。
        /// </summary>
        /// <param name="適用する処理">
        ///		引数は、順に、対象のチップ, チップ番号, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離dpx。
        ///		時間と距離はいずれも、負数ならバー未達、0でバー直上、正数でバー通過。
        ///	</param>
        private void _描画範囲内のすべてのチップに対して( double 現在の演奏時刻sec, Action<チップ, int, double, double, double> 適用する処理 )
        {
            var スコア = App.演奏スコア;
            if( null == スコア )
                return;

            for( int i = this._描画開始チップ番号; ( 0 <= i ) && ( i < スコア.チップリスト.Count ); i++ )
            {
                var チップ = スコア.チップリスト[ i ];

                // ヒット判定バーとチップの間の、時間 と 距離 を算出。→ いずれも、負数ならバー未達、0でバー直上、正数でバー通過。
                double ヒット判定バーと描画との時間sec = 現在の演奏時刻sec - チップ.描画時刻sec;
                double ヒット判定バーと発声との時間sec = 現在の演奏時刻sec - チップ.発声時刻sec;
                double 倍率 = this._譜面スクロール速度.補間付き速度;
                double ヒット判定バーとの距離dpx = this._指定された時間secに対応する符号付きピクセル数を返す( 倍率, ヒット判定バーと描画との時間sec );

                // 終了判定。
                bool チップは画面上端より上に出ている = ( ( ヒット判定位置Ydpx + ヒット判定バーとの距離dpx ) < -40.0 );   // -40 はチップが隠れるであろう適当なマージン。
                if( チップは画面上端より上に出ている )
                    break;

                // 処理実行。開始判定（描画開始チップ番号の更新）もこの中で。
                適用する処理( チップ, i, ヒット判定バーと描画との時間sec, ヒット判定バーと発声との時間sec, ヒット判定バーとの距離dpx );
            }
        }

        private double _指定された時間secに対応する符号付きピクセル数を返す( double speed, double 指定時間sec )
        {
            const double _1ミリ秒あたりのピクセル数 = 0.14625 * 2.25 * 1000.0;    // これを変えると、speed あたりの速度が変わる。

            return ( 指定時間sec * _1ミリ秒あたりのピクセル数 * speed );
        }


        private void _チップのヒット処理を行う( チップ chip, 判定種別 judge, bool 再生, bool 判定, bool 非表示, double ヒット判定バーと発声との時間sec )
        {
            this._チップの演奏状態[ chip ].ヒット済みである = true;

            if( 再生 && ( judge != 判定種別.MISS ) )
            {
                #region " チップの発声がまだなら行う。"
                //----------------
                // チップの発声時刻は、描画時刻と同じかそれより過去に位置するので、ここに来た時点で未発声なら発声していい。
                // というか発声時刻が過去なのに未発声というならここが最後のチャンスなので、必ず発声しないといけない。
                if( !( this._チップの演奏状態[ chip ].発声済みである ) )
                {
                    this._チップの発声を行う( chip, ヒット判定バーと発声との時間sec );
                    this._チップの演奏状態[ chip ].発声済みである = true;
                }
                //----------------
                #endregion
            }
            if( 判定 )
            {
                #region " チップの判定処理を行う。"
                //----------------
                var 対応表 = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理[ chip.チップ種別 ];

                if( judge != 判定種別.MISS )
                {
                    // MISS以外（PERFECT～OK）
                    this._チップ光.表示を開始する( 対応表.表示レーン種別 );
                    this._ドラムパッド.ヒットする( 対応表.表示レーン種別 );
                    this._レーンフラッシュ.開始する( 対応表.表示レーン種別 );
                }

                this._判定文字列.表示を開始する( 対応表.表示レーン種別, judge );
                this.成績.ヒット数を加算する( judge );
                //----------------
                #endregion
            }
            if( 非表示 )
            {
                #region " チップを非表示にする。"
                //----------------
                if( judge == 判定種別.MISS )
                {
                    // MISSチップは最後まで表示し続ける。
                }
                else
                {
                    // PERFECT～POOR チップは非表示。
                    this._チップの演奏状態[ chip ].可視 = false;
                }
                //----------------
                #endregion
            }
        }

        private void _チップの発声を行う( チップ chip, double 再生開始位置sec )
        {
            if( chip.チップ種別 == チップ種別.背景動画 )
            {
                #region " (A) AVI動画を再生する。"
                //----------------
                int AVI番号 = chip.チップサブID;

                if( App.AVI管理.動画リスト.TryGetValue( AVI番号, out Video video ) )
                {
                    App.サウンドタイマ.一時停止する();       // 止めても止めなくてもカクつくだろうが、止めておけば譜面は再開時にワープしない。

                    video.再生を開始する();

                    App.サウンドタイマ.再開する();
                }
                //----------------
                #endregion
            }
            else if( 0 == chip.チップサブID )
            {
                #region " (B) SSTF準拠のドラムサウンドを再生する。"
                //----------------
                var prop = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理.チップtoプロパティ[ chip.チップ種別 ];

                // ドラムサウンドを持つチップなら発声する。（持つかどうかはこのメソッド↓内で判定される。）
                App.ドラムサウンド.発声する( chip.チップ種別, 0, prop.発声前消音, prop.消音グループ種別, ( chip.音量 / (float) チップ.最大音量 ) );
                //----------------
                #endregion
            }
            else
            {
                #region " (C) WAVサウンドを再生する。"
                //----------------
                int WAV番号 = chip.チップサブID;
                var prop = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理.チップtoプロパティ[ chip.チップ種別 ];

                // WAVを持つチップなら発声する。（持つかどうかはこのメソッド↓内で判定される。）
                App.WAV管理.発声する( chip.チップサブID, chip.チップ種別, prop.発声前消音, prop.消音グループ種別, ( chip.音量 / (float) チップ.最大音量 ) );
                //----------------
                #endregion
            }
        }

        private チップ _指定された時刻に一番近いチップを返す( double 時刻sec, ドラム入力種別 drumType )
        {
            var チップtoプロパティ = App.ユーザ管理.ログオン中のユーザ.ドラムチッププロパティ管理.チップtoプロパティ;

            var 一番近いチップ = (チップ) null;
            var 一番近いチップの時刻差の絶対値sec = (double) 0.0;

            for( int i = 0; i < App.演奏スコア.チップリスト.Count; i++ )
            {
                var chip = App.演奏スコア.チップリスト[ i ];

                if( チップtoプロパティ[ chip.チップ種別 ].ドラム入力種別 != drumType )
                    continue;   // 指定されたドラム入力種別ではないチップは無視。

                if( null != 一番近いチップ )
                {
                    var 今回の時刻差の絶対値sec = Math.Abs( chip.描画時刻sec - 時刻sec );

                    if( 一番近いチップの時刻差の絶対値sec < 今回の時刻差の絶対値sec )
                    {
                        // 時刻差の絶対値が前回より増えた → 前回のチップが指定時刻への再接近だった
                        break;
                    }
                }

                一番近いチップ = chip;
                一番近いチップの時刻差の絶対値sec = Math.Abs( 一番近いチップ.描画時刻sec - 時刻sec );
            }

            return 一番近いチップ;
        }

        private void _キャプチャ画面を描画する( DeviceContext1 dc, float 不透明度 = 1.0f )
        {
            Debug.Assert( null != this.キャプチャ画面, "キャプチャ画面が設定されていません。" );

            グラフィックデバイス.Instance.D2DBatchDraw( dc, () => {
                dc.DrawBitmap(
                    this.キャプチャ画面,
                    new RectangleF( 0f, 0f, グラフィックデバイス.Instance.設計画面サイズ.Width, グラフィックデバイス.Instance.設計画面サイズ.Height ),
                    不透明度,
                    BitmapInterpolationMode.Linear );
            } );
        }


        private bool _初めての進行描画 = true;


        private class YAMLマップ_ドラムチップ
        {
            public Dictionary<string, float[]> 矩形リスト { get; set; }
            public float 縦方向中央位置 { get; set; }
        }
    }
}
