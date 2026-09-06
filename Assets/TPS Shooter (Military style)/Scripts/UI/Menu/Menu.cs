using UnityEngine;
using LightDev;
using LightDev.UI;
using XH;

namespace TPSShooter.UI.Menu
{
    public class Menu : CanvasElement
    {
        public override void Subscribe()
        {
            Events.SceneLoaded += Show;
            Events.RequestMenu += Show;
        }

        public override void Unsubscribe()
        {
            Events.SceneLoaded -= Show;
            Events.RequestMenu -= Show;
        }

        // startgame: 点击开始游戏按钮时调用一次（从主菜单进入武器选择）
        public void OnPlay()
        {
            xh.api.Ad.ShowInsert("startgame");
            Events.MenuClickSound.Call();
            Events.RequestMenuWeapon.Call();
            Hide();
        }

        // common: 切换界面时调用一次（主菜单 -> 设置）
        public void OnSettings()
        {
            xh.api.Ad.ShowInsert("common");
            Events.MenuClickSound.Call();
            Events.RequestMenuSettings.Call();
            Hide();
        }

        public void OnExit()
        {
            // Application.Quit();
            xh.app.ExitGame();
        }
    }
}
