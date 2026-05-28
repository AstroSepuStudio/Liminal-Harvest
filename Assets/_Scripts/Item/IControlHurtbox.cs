public interface IControlHurtbox 
{
    public ItemBase GetOwner();
    public AttackStat GetAttackStat();

    public void HurtBoxHit();
    public void HurtBoxHurt(EntityStats target);
}
