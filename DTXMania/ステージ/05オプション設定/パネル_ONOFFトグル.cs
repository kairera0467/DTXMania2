﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FDK;

namespace DTXMania.ステージ.オプション設定
{
    /// <summary>
    ///		OFF と ON を切り替えられるスイッチ。
    /// </summary>
    class パネル_ONOFFトグル : パネル_文字列リスト
    {
        public bool ONである
        {
            get => !( this.OFFである );
            set => this.OFFである = !value;
        }
        public bool OFFである
        {
            get => ( 0 == this.現在選択されている選択肢の番号 );
            set => this.現在選択されている選択肢の番号 = ( value ) ? 0 : 1;
        }

        public パネル_ONOFFトグル( string パネル名, bool 初期状態はON, Action<パネル> 値の変更処理 = null )
            : base( パネル名, new[] { "OFF", "ON" }, ( 初期状態はON ) ? 1 : 0, 値の変更処理 )
        {
            //using( Log.Block( FDKUtilities.現在のメソッド名 ) )
            {
                Log.Info( $"ONOFFトグルパネルを生成しました。[{this}]" );
            }
        }

        public override string ToString()
            => $"{this.パネル名}, 初期状態: {( this.ONである ? "ON" : "OFF" ) }";
    }
}
