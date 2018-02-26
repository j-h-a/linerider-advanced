﻿//
//  Editor.cs
//
//  Author:
//       Noah Ablaseau <nablaseau@hotmail.com>
//
//  Copyright (c) 2017 
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using linerider.Drawing;
using linerider.Rendering;
using linerider.Game;
using OpenTK;
using System.IO;
using System.Threading;
using Gwen.Controls;
using linerider.Tools;
using linerider.Audio;
using linerider.Utils;
using linerider.Lines;
using linerider.IO;

namespace linerider
{
    /// <summary>The interface for communicating with the game track object</summary>
    public class Editor : GameService
    {
        private float _oldZoom = 1.0f;
        private RiderFrame _flag;
        private Track _track;
        private int _startFrame;
        private int _iteration = 6;
        private bool _renderriderinvalid = true;
        private RiderFrame _renderrider = null;
        private ResourceSync _tracksync = new ResourceSync();
        private ResourceSync _renderridersync = new ResourceSync();
        private SimulationRenderer _renderer = new SimulationRenderer();
        private bool _refreshtrack = false;

        public readonly Stopwatch FramerateWatch = new Stopwatch();
        public readonly FPSCounter FramerateCounter = new FPSCounter();
        /// <summary>
        /// indicates if we're in playbackmode
        /// </summary>
        public bool PlaybackMode { get; private set; }
        private bool _paused = false;
        public float Zoom = Constants.DefaultZoom;
        public Camera Camera { get; private set; }
        public List<LineTrigger> ActiveTriggers = new List<LineTrigger>();
        public int CurrentFrame => PlaybackMode ? Offset + _startFrame : 0;
        public int LineCount => _track.Lines.Count;
        public bool SimulationNeedsDraw = false;
        public bool RequiresUpdate
        {
            get
            {
                return _renderer.RequiresUpdate;
            }
            set
            {
                _renderer.RequiresUpdate = value;
            }
        }
        public bool ZeroStart
        {
            get { return _track.ZeroStart; }
            set { _track.ZeroStart = value; }
        }
        public RiderFrame RenderRiderInfo
        {
            get
            {
                if (_renderriderinvalid)
                {
                    _renderriderinvalid = false;
                    _renderrider = Timeline.ExtractFrame(Offset, IterationsOffset);
                }
                return _renderrider;
            }
        }
        public Rider RenderRider
        {
            get
            {
                return RenderRiderInfo.State;
            }
        }
        public string Name
        {
            get
            {
                return _track.Name;
            }
        }

        public bool Paused
        {
            get
            {
                return _paused;
            }
            private set
            {
                if (value == _paused)
                    return;
                _paused = value;
            }
        }
        public UndoManager UndoManager { get; private set; }
        public bool Playing => PlaybackMode && !Paused;
        /// <summary>
        /// The offset between 0-6 of the currently rendered iteration
        /// </summary>
        public int IterationsOffset
        {
            get
            {
                return _iteration;
            }
            set
            {
                if (IterationsOffset > 6 || IterationsOffset < 0)
                    throw new Exception("iteration num out of range");
                _iteration = value;
            }
        }
        /// <summary>
        /// The current number of frames since start (incl flag)
        /// </summary>
        public int Offset { get; private set; }

        public Timeline Timeline { get; private set; }
        public Editor()
        {
            Camera = new Camera();
            _track = new Track();
            Timeline = new Timeline(_track);
            _track.ActiveTriggers = ActiveTriggers;
            Offset = 0;
            UndoManager = new UndoManager();
        }
        public void Render(float blend)
        {
            if (_refreshtrack)
            {
                RefreshTrack();
                _refreshtrack = false;
            }
            DrawOptions drawOptions = new DrawOptions();
            drawOptions.DrawFlag = _flag != null;
            if (drawOptions.DrawFlag)
            {
                drawOptions.FlagRider = _flag.State;
            }
            drawOptions.Blend = blend;
            drawOptions.NightMode = Settings.NightMode;
            drawOptions.GravityWells = Settings.Local.RenderGravityWells;
            drawOptions.LineColors = !Settings.Local.PreviewMode && (!Playing || Settings.Local.ColorPlayback);
            drawOptions.KnobState = KnobState.Hidden;
            if (!Playing && game.SelectedTool is MoveTool movetool)
            {
                drawOptions.KnobState = movetool.CanLifelock
                ? KnobState.LifeLock
                : KnobState.Shown;
            }
            drawOptions.Paused = Paused;
            drawOptions.Playback = PlaybackMode;
            drawOptions.Rider = RenderRiderInfo.State;
            drawOptions.ShowContactLines = Settings.Local.DrawContactPoints;
            drawOptions.ShowMomentumVectors = Settings.Local.MomentumVectors;
            drawOptions.Zoom = Zoom;
            drawOptions.RiderDiagnosis = RenderRiderInfo.Diagnosis;
            if (Settings.SmoothPlayback && Playing && Offset > 0 && blend < 1)
            {
                //interpolate between last frame and current one
                drawOptions.Rider = Rider.Lerp(Timeline.GetFrame(Offset - 1), Timeline.GetFrame(Offset), blend);
            }
            var changes = Timeline.HitTest.SetFrame(Offset);
            foreach (var change in changes)
            {
                GameLine line;
                if (_track.LineLookup.TryGetValue(change, out line))
                {
                    _renderer.RedrawLine(line);
                }
            }

            _renderer.Render(_track, Timeline, Camera, drawOptions);
        }

        /// <summary>
        /// Function to be called after updating the playback buffer
        /// </summary>
        public void InvalidateRenderRider()
        {
            _renderriderinvalid = true;
            SimulationNeedsDraw = true;
        }
        public Rider GetStart()
        {
            return _track.GetStart();
        }
        public void Reset()
        {
            Timeline.Restart(_track.GetStart());
            SetFrame(0);
        }
        public bool FastGridCheck(double x, double y)
        {
            var chunk = _track.QuickGrid.GetCellFromPoint(new Vector2d(x, y));
            return chunk != null && chunk.Count != 0;
        }
        public bool GridCheck(double x, double y)
        {
            var chunk = _track.Grid.PointToChunk(new Vector2d(x, y));
            return chunk != null && chunk.Count != 0;
        }
        /// <summary>
        /// Function for indicating the physics of the track have changed, so inform buffermanager
        /// </summary>
        public void NotifyTrackChanged()
        {
            if (PlaybackMode)
            {
                Timeline.NotifyChanged();
                InvalidateRenderRider();
            }
            game.Canvas.DisableFlagTooltip();
            _renderer.RequiresUpdate = true;
        }
        internal void RestoreFlag(RiderFrame flag)
        {
            _flag = flag;
        }

        public void Flag()
        {
            if (PlaybackMode)
            {
                _flag = new RiderFrame { State = Timeline.GetFrame(Offset), FrameID = CurrentFrame };
            }
            else
            {
                _flag = null;
            }

            game.Canvas.DisableFlagTooltip();
            game.Invalidate();
        }
        /// <summary>
        /// Redraws the entire track in the renderer.
        /// useful for night mode toggle
        /// </summary>
        public void RefreshTrack()
        {
            _renderer.RefreshTrack(_track);
        }

        public void NextFrame()
        {
            SetFrame(++Offset);
        }

        public void PreviousFrame()
        {
            SetFrame(Math.Max(0, Offset - 1));
        }

        public void Stop()
        {
            if (PlaybackMode)
            {
                Timeline.HitTest.Reset();
                PlaybackMode = false;
                Paused = false;

                Zoom = _oldZoom;
                Camera.Pop();
                Reset();
                foreach (var v in ActiveTriggers)
                {
                    v.Reset();
                }
                ActiveTriggers.Clear();
                game.Canvas.HidePlaybackUI();
                game.Invalidate();
                game.Track.SimulationNeedsDraw = true;
            }
        }

        public void TogglePause()
        {
            if (PlaybackMode)
            {
                Paused = !Paused;
                game.Canvas.UpdatePauseUI();
                game.Canvas.UpdateIterationUI();
                game.Scheduler.Reset();
                SimulationNeedsDraw = true;
            }
        }
        public void StartFromFlag()
        {
            Timeline.HitTest.Reset();
            if (_flag == null)
            {
                Timeline.Restart(_track.GetStart());
            }
            else
            {
                Timeline.Restart(_flag.State);
                _startFrame = _flag.FrameID;
            }
            Start(0);
        }
        public void StartIgnoreFlag()
        {
            Timeline.HitTest.Reset();
            Timeline.Restart(_track.GetStart());
            Start(0);
        }
        public void ResumeFromFlag()
        {
            Timeline.HitTest.Reset();
            Timeline.Restart(_track.GetStart());
            if (_flag != null)
            {
                var atflag = Timeline.GetFrame(_flag.FrameID);
                game.Canvas.SetFlagState(atflag.Body.CompareTo(_flag.State.Body));
            }
            Start(Timeline.Length - 1);
        }
        private void Start(int frameid)
        {
            if (frameid >= Timeline.Length)
                throw new Exception("Start frame out of range");
            if (!PlaybackMode)
            {
                _oldZoom = Zoom;
                Camera.Push();
            }
            PlaybackMode = true;
            Paused = false;
            _startFrame = 0;
            Offset = frameid;
            IterationsOffset = 6;
            Camera.SetFrameCenter(Timeline.GetFrame(frameid).CalculateCenter());

            game.Canvas.ShowPlaybackUI();
            game.UpdateCursor();
            switch (Settings.PlaybackZoomType)
            {
                case 0: //current
                    break;

                case 1: //default
                    game.Track.Zoom = Constants.DefaultZoom;
                    break;

                case 2: //specific
                    game.Track.Zoom = Settings.PlaybackZoomValue;
                    break;
            }
            game.Scheduler.Reset();
            game.Canvas.UpdateScrubber();
            InvalidateRenderRider();
            SimulationNeedsDraw = true;
        }
        public void SetFrame(int frame, bool updateslider = true)
        {
            Offset = frame;
            Timeline.GetFrame(frame);
            IterationsOffset = 6;
            InvalidateRenderRider();

            game.Canvas.UpdateIterationUI();
            if (updateslider)
            {
                game.Canvas.UpdateScrubber();
            }
            game.Invalidate();
        }

        public void Update(int times)
        {
            var slider = (HorizontalIntSlider)game.Canvas.FindChildByName("timeslider");
            if (slider.Held)
            {
                UpdateCamera();
                return;
            }
            for (var i = 0; i < times; i++)
            {
                NextFrame();
                for (var ix = ActiveTriggers.Count - 1; ix >= 0; --ix)
                    if (ActiveTriggers[ix].Activate())
                        ActiveTriggers.RemoveAt(ix);
                UpdateCamera();
            }
        }

        public void UpdateCamera()
        {
            Camera.SetFrame(RenderRider);
            if (Settings.SmoothCamera)
            {
                Rider prediction;
                using (var trk = CreateTrackReader())
                {
                    prediction = trk.TickBasic(RenderRider);
                }
                Camera.SetPrediction(prediction.CalculateCenter());

            }
            SimulationNeedsDraw = true;
        }

        public void BackupTrack(bool Crash = true)
        {
            try
            {
                if (_track.Lines.Count == 0)
                    return;
                game.Loading = true;
                using (var trk = CreateTrackReader())
                {
                    if (Crash)
                    {
                        TrackIO.SaveTrackToFile(_track, "Crash Backup", Settings.Local.CurrentSong?.ToString());
                    }
                    else
                    {
                        // make sure were not saving 0 changes
                        if (UndoManager.HasChanges())
                        {
                            TrackIO.CreateAutosave(_track, Settings.Local.CurrentSong?.ToString());
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Debug.WriteLine("Autosave exception: "+e);
            }
            finally
            {
                game.Loading = false;
                game.Invalidate();
            }
        }

        public void ChangeTrack(Track trk)
        {
            using (_tracksync.AcquireWrite())
            {
                _flag = null;
                if (_track != null && _track.ActiveTriggers == ActiveTriggers)
                    _track.ActiveTriggers = null;
                _track = trk;
                _track.ActiveTriggers = ActiveTriggers;
            }
            Timeline = new Timeline(trk);
            UndoManager = new UndoManager();
            _refreshtrack = true;
            Reset();
            Camera.SetFrameCenter(trk.StartOffset);
            GC.Collect();//this is the safest place to collect
        }
        public void AutoLoadPrevious()
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    using (_tracksync.AcquireWrite())
                    {
                        game.Loading = true;
                        var lasttrack = Settings.LastSelectedTrack;
                        var trdr = Constants.TracksDirectory;
                        if (!lasttrack.StartsWith(trdr))
                            return;
                        if (string.Equals(
                            Path.GetExtension(lasttrack),
                            ".trk",
                            StringComparison.InvariantCultureIgnoreCase))
                        {
                            var trackname = TrackIO.GetTrackName(lasttrack);

                            ChangeTrack(TRKLoader.LoadTrack(lasttrack, trackname));
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Autoload failure: " + e.ToString());
                }
                finally
                {
                    game.Loading = false;
                }
            });
        }
        public TrackWriter CreateTrackWriter()
        {
            return TrackWriter.AcquireWrite(_tracksync, _track, _renderer, UndoManager, Timeline);
        }
        public TrackReader CreateTrackReader()
        {
            return TrackReader.AcquireRead(_tracksync, _track);
        }

        internal RiderFrame GetFlag()
        {
            return _flag;
        }
    }
}