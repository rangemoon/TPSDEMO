using UnityEngine;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class PlayerHitMarker : CanvasElement
    {
        public GameObject hitMarkerPrefab;
        private PlayerBehaviour player;

        private void Start()
        {
            player = PlayerBehaviour.GetInstance();
        }

        public override void Subscribe()
        {
            Events.SceneLoaded += Show;
            Events.GamePaused += Hide;
            Events.GameResumed += Show;
            Events.PlayerDied += Hide;
            Events.PlayerBulletHit += OnPlayerBulletHit;
            Events.PlayerZombieHit += OnPlayerZombieHit;
        }

        public override void Unsubscribe()
        {
            Events.SceneLoaded -= Show;
            Events.GamePaused -= Hide;
            Events.GameResumed -= Show;
            Events.PlayerDied -= Hide;
            Events.PlayerBulletHit -= OnPlayerBulletHit;
            Events.PlayerZombieHit -= OnPlayerZombieHit;
        }

        private void OnPlayerBulletHit(EnemyBullet bullet)
        {
            CreateHitMarkerObject(bullet.MasterOfBullet.position);
        }

        private void OnPlayerZombieHit(ZombieBehaviour zombie)
        {
            CreateHitMarkerObject(zombie.GetPosition());
        }

        private void CreateHitMarkerObject(Vector3 enemyPos)
        {
            Vector3 relative = player.transform.InverseTransformPoint(enemyPos);
            float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;

            GameObject marker = GamePool.Spawn(hitMarkerPrefab, transform.position, Quaternion.identity);
            marker.transform.SetParent(transform, false);
            marker.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, -angle));

            // Auto-return to pool after 1 second
            var lifetime = marker.GetComponent<PooledLifetime>();
            if (lifetime == null) lifetime = marker.AddComponent<PooledLifetime>();
            lifetime.delay = 1f;
        }
    }
}
