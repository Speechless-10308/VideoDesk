using VideoDesk.Core;

namespace VideoDesk.App;

public sealed partial class MainWindow
{
    private async Task LoadStartup()
    {
        await Run(async()=>
        {
            try{account=await api.GetAccount(navigation.Token);}
            catch(Exception error) when(error is not OperationCanceledException){account=new(false,"","",0,false);Notify("账号状态暂不可用："+error.Message);}
            UpdateAccount();
        });
        if(closingStarted)return;
        await Run(ShowHome);
    }
}
