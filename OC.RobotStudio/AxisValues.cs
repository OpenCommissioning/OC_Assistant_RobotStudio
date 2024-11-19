using ABB.Robotics.Controllers.MotionDomain;

namespace OC.RobotStudio;

internal class AxisValues
{
    public float[] Axis { get; } = new float[6];
    public int NumberOfAxes { get; private set; }
    
    public void Set(MechanicalUnit mechanicalUnit)
    {
        for (var i = 0; i < 6; i++)
        {
            Axis[i] = 0f;
        }

        NumberOfAxes = 0;
        
        var jointTarget = mechanicalUnit.GetPosition();
        if (mechanicalUnit.NumberOfAxes > 6) return;
        
        NumberOfAxes = mechanicalUnit.NumberOfAxes;
            
        if (mechanicalUnit.Type == MechanicalUnitType.TcpRobot)
        {
            Axis[0] = jointTarget.RobAx.Rax_1;
            Axis[1] = jointTarget.RobAx.Rax_2;
            Axis[2] = jointTarget.RobAx.Rax_3;
            Axis[3] = jointTarget.RobAx.Rax_4;
            Axis[4] = jointTarget.RobAx.Rax_5;
            Axis[5] = jointTarget.RobAx.Rax_6;
            return;
        }
            
        Axis[0] = jointTarget.ExtAx.Eax_a;
        Axis[1] = jointTarget.ExtAx.Eax_b;
        Axis[2] = jointTarget.ExtAx.Eax_c;
        Axis[3] = jointTarget.ExtAx.Eax_d;
        Axis[4] = jointTarget.ExtAx.Eax_e;
        Axis[5] = jointTarget.ExtAx.Eax_f;
    }
}