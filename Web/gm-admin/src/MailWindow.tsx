import { nf } from './format';
import { SlotFace, TipWrap, rarityClass } from './itemInfo';
import type { ItemCatalogDetails, ItemEquipmentOptions } from './items';

/* 游戏内「查看邮件」窗口的仿制预览：界面用 CSS / SVG 重绘，不使用客户端素材。
   官方邮件的发件人名在服务端为空，「DNF管理员」由客户端显示。 */
export type MailSlot = { itemId: number; count: number; name: string; rarity: number | null; equipment?: ItemEquipmentOptions | null; details?: ItemCatalogDetails };
function GoldCoins() {
  return <svg viewBox="0 0 26 20" width="24" height="18" aria-hidden="true">
    <ellipse cx="13" cy="14.5" rx="10" ry="4.2" fill="#9a640a" /><ellipse cx="13" cy="12.6" rx="10" ry="4.2" fill="#ffcf45" stroke="#7a4c00" strokeWidth=".8" />
    <ellipse cx="13" cy="12.6" rx="6" ry="2.2" fill="none" stroke="#c98a10" strokeWidth=".8" />
    <ellipse cx="11" cy="7.8" rx="7.5" ry="3.3" fill="#9a640a" /><ellipse cx="11" cy="6.2" rx="7.5" ry="3.3" fill="#ffe17a" stroke="#7a4c00" strokeWidth=".8" />
    <path d="M7.5 5.2 q3.5 -1.6 7 0" fill="none" stroke="#fff6c8" strokeWidth=".9" strokeLinecap="round" />
  </svg>;
}
export function MailWindow({ text, slots, gold }: { text: string; slots: MailSlot[]; gold: number }) {
  return <div className="mw-stage">
    <div className="mw" role="img" aria-label="游戏内查看邮件窗口预览">
      <div className="mw-title">查看邮件<span className="mw-x">✕</span></div>
      <div className="mw-body">
        <div className="mw-from"><span className="k">发件人：</span><span className="v">DNF管理员</span></div>
        <div className="mw-text" data-testid="mail-window-text">{text}<span className="mw-scroll" aria-hidden="true"><i>▲</i><span className="track" /><i>▼</i></span></div>
        <div className="mw-grid">{Array.from({ length: 10 }, (_, index) => { const slot = slots[index];
          if (!slot) return <span className="mw-slot" key={index}>附件</span>;
          const growth = slot.equipment && slot.equipment.growth !== 'none' ? `+${slot.equipment.level}` : '';
          return <TipWrap key={index} itemId={slot.itemId} brief={{ name: slot.name, rarity: slot.rarity }} equip={slot.equipment} capabilities={slot.details?.equipment}>
            <span className={`mw-slot f ${rarityClass(slot.rarity)}`} tabIndex={0} aria-label={`${slot.name} 的说明`}>{growth && <span className="lv">{growth}</span>}
              <SlotFace itemId={slot.itemId} name={slot.name} />{slot.count > 1 && <span className="cnt">{slot.count > 9999 ? '9999+' : slot.count}</span>}</span></TipWrap>; })}</div>
        <div className="mw-money"><GoldCoins /><b>{nf(gold)}</b><span className="mw-btn">全部接收</span></div>
      </div>
      <div className="mw-foot"><span className="mw-btn off">回复</span><span className="mw-btn">保存</span><span className="mw-btn">删除</span><span className="mw-btn">关闭</span></div>
    </div>
  </div>;
}
