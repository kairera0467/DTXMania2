﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FDK
{
    /// <summary>
    ///		指定されたメディアファイル（動画, 音楽）を最初に一括して読み込み＆デコードする <see cref="CSCore.IWaveSource"/> オブジェクトを生成する。
    /// </summary>
    public class MediaFoundationOnMemoryWaveSource : MediaFoundationOnStreamingWaveSource
    {
        /// <summary>
        ///		コンストラクタ。
        ///		指定されたファイルを指定されたフォーマットでデコードし、内部にオンメモリで保管する。
        /// </summary>
        public MediaFoundationOnMemoryWaveSource( VariablePath ファイルパス, CSCore.WaveFormat deviceFormat )
            : base( ファイルパス, deviceFormat )
        {
            // 読み込み＆デコード完了まで待機。
            if( !( this._デコードタスク.Wait( 60 * 1000 ) ) )
                throw new TimeoutException( $"デコードタスクがタイムアウトしました。[{ファイルパス.変数付きパス}]" );
        }
    }
}
