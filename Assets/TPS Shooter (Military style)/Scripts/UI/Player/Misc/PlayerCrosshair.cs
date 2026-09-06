using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
    public class PlayerCrosshair : CanvasElement
    {
        [Header("References")]
        public Image peacefulCrosshair;
        public Image enemyCrosshair;

        [Header("Preferences")]
        public bool showCrosshairWhileRunning = true;

        private PlayerBehaviour _player;

        // Cache: only re-check GetComponentInParent when FireHitObject reference changes
        private Transform _cachedFireHitObject;
        private bool _cachedIsEnemy;

        private void Start()
        {
            TryBindPlayer();
        }

        /// <summary>
        /// 绑定本机玩家；联机生成时本地玩家可能尚未就绪，之后每帧继续尝试直到绑定成功。
        /// </summary>
        private void TryBindPlayer()
        {
            _player = PlayerBehaviour.GetLocalPlayer();
        }

        public override void Subscribe()
        {
            Events.SceneLoaded += Show;
            Events.GamePaused += Hide;
            Events.GameResumed += Show;
            Events.PlayerDied += Hide;
        }

        public override void Unsubscribe()
        {
            Events.SceneLoaded -= Show;
            Events.GamePaused -= Hide;
            Events.GameResumed -= Show;
            Events.PlayerDied -= Hide;
        }

        private void UpdateAim()
        {
            if ((!showCrosshairWhileRunning && _player.IsRunning) ||
              _player.IsUnarmedMode ||
              _player.IsThrowingGrenade ||
              _player.IsDrivingVehicle ||
              (_player.IsAiming && _player.CurrentWeaponBehaviour.ScopeSettings.IsFPS)
            )
            {
                enemyCrosshair.enabled = false;
                peacefulCrosshair.enabled = false;
            }
            else
            {
                // Only call GetComponentInParent when FireHitObject actually changes
                Transform currentHitObject = _player.FireHitObject;
                if (currentHitObject != _cachedFireHitObject)
                {
                    _cachedFireHitObject = currentHitObject;
                    _cachedIsEnemy = currentHitObject != null
                      && (currentHitObject.GetComponentInParent<EnemyBehaviour>() != null
                        || currentHitObject.GetComponentInParent<ZombieBehaviour>() != null);
                }

                if (_cachedIsEnemy)
                {
                    enemyCrosshair.enabled = true;
                    peacefulCrosshair.enabled = false;
                }
                else
                {
                    enemyCrosshair.enabled = false;
                    peacefulCrosshair.enabled = true;
                }
            }
        }

        private void Update()
        {
            if (_player == null)
                TryBindPlayer();
            if (_player == null)
                return;

            if (_player.IsAlive)
                UpdateAim();
        }
    }
}
