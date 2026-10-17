namespace VideoDesk.Core;

// Decoder timestamps are quantized. Small drift changes velocity, not glyph position.
public sealed class PlaybackClock
{
    private double lastWall,sampleWall,samplePosition,speed=1;
    private bool initialized,paused=true;
    public double Position {get;private set;}
    public int Discontinuities {get;private set;}
    public void Reset(){initialized=false;paused=true;}
    public void Synchronize(double position,bool isPaused,double playbackSpeed,double wall)
    {
        Advance(wall);
        if(!initialized||Math.Abs(position-Position)>.4){Position=Math.Max(0,position);if(initialized)Discontinuities++;}
        initialized=true;lastWall=sampleWall=wall;samplePosition=position;paused=isPaused;speed=Math.Clamp(playbackSpeed,.25,4);
    }
    public double Advance(double wall)
    {
        if(!initialized)return Position;
        var dt=Math.Max(0,wall-lastWall);lastWall=wall;
        if(!paused){var predicted=Position+dt*speed;var error=samplePosition+(wall-sampleWall)*speed-predicted;var correction=Math.Clamp(error*3,-speed*.2,speed*.2);Position=predicted+dt*correction;}
        return Position;
    }
}
