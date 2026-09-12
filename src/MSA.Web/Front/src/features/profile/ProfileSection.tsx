import { Icon } from '../../shared/ui/Icon';
import type { UserProfile } from './types';

export function ProfileSection({ profile, onEdit }: { profile: UserProfile; onEdit: () => void }) {
  return (
    <section className="account-section profile-section" id="profile">
      <div className="account-section__heading">
        <h2>个人资料</h2>
        <button className="outline-action" type="button" onClick={onEdit}>
          <Icon name="edit" />
          编辑资料
        </button>
      </div>
      <dl className="profile-list">
        <div><dt>姓名</dt><dd>{profile.displayName}</dd></div>
        <div><dt>邮箱</dt><dd>{profile.email}</dd></div>
        <div>
          <dt>手机号</dt>
          <dd>
            {profile.phoneNumber
              ? `${profile.phoneNumber}（${profile.phoneNumberConfirmed ? '已验证' : '未验证'}）`
              : '未填写'}
          </dd>
        </div>
      </dl>
    </section>
  );
}
