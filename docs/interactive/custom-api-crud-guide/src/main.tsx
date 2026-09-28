import * as React from 'react';
import { useState } from 'react';
import { render } from 'react-dom';
import { lessons, sources } from './content';
import { samples, Sample } from './snippets';
import './styles.css';

const progressKey = 'fmc-crud-guide-progress-v1';
function initialProgress(): string[] {
  try { const value = JSON.parse(localStorage.getItem(progressKey) || '[]'); return Array.isArray(value) ? value.filter(x => lessons.some(l => l.id === x)) : []; }
  catch { return []; }
}
function Code({ sample }: { sample: Sample }) {
  const [copied, setCopied] = useState(false);
  const [copyError, setCopyError] = useState(false);
  async function copy() {
    try { await navigator.clipboard.writeText(sample.code); setCopied(true); setCopyError(false); setTimeout(() => setCopied(false), 2000); }
    catch { setCopyError(true); }
  }
  return <section className="code-card">
    <div className="code-heading"><div><span className="eyebrow">CODE MẪU · {sample.language}</span><h3>{sample.title}</h3></div><button className="copy" onClick={copy}>{copied ? '✓ Đã copy' : 'Copy code'}</button></div>
    <div className="file-path">{sample.path}</div>
    <p className="code-note">{sample.note}</p>
    {copyError && <p role="alert">Không truy cập được clipboard. Bạn có thể chọn và copy code bên dưới.</p>}
    <pre className="source-code"><code>{sample.code.split('\n').map((line, i) => <span className={'code-line ' + (line.trim().startsWith('//') ? 'comment' : '')} key={i}><span aria-hidden="true" className="line-no">{i + 1}</span><span>{line || ' '}</span></span>)}</code></pre>
    <div className="explain"><h4>Đọc đoạn code này như thế nào?</h4><ul>{sample.explain.map(x => <li key={x}>{x}</li>)}</ul></div>
  </section>;
}

type DemoIssue = { id: string; title: string; status: string; version: number };
const initialRows: DemoIssue[] = [
  { id: 'ISS-001', title: 'Nhiệt độ phòng máy cao', status: 'Draft', version: 1 },
  { id: 'ISS-002', title: 'Kiểm tra tín hiệu TEMP-002', status: 'Open', version: 3 },
];
function Playground() {
  const [rows, setRows] = useState<DemoIssue[]>(initialRows.map(x => ({ ...x })));
  const [selected, setSelected] = useState<DemoIssue | null>(null);
  const [title, setTitle] = useState('');
  const [status, setStatus] = useState('Draft');
  const [log, setLog] = useState('Chọn một issue để Read, hoặc nhập tiêu đề rồi Create.');
  const [seq, setSeq] = useState(3);
  const [confirmDelete, setConfirmDelete] = useState(false);
  function read(row: DemoIssue) { setSelected({ ...row }); setTitle(row.title); setStatus(row.status); setConfirmDelete(false); setLog('GET fmc_GetPointIssue → ' + row.id + ' · RowVersion ' + row.version); }
  function create() {
    if (!title.trim() || title.trim().length > 200) { setLog('REJECT · Title bắt buộc, tối đa 200 ký tự.'); return; }
    const row = { id: 'ISS-' + String(seq).padStart(3, '0'), title: title.trim(), status: 'Draft', version: 1 };
    setRows([...rows, row]); setSeq(seq + 1); setSelected(row); setStatus('Draft'); setConfirmDelete(false);
    setLog('POST fmc_CreatePointIssue → ' + row.id + ' · Draft. GET lại để lấy RowVersion 1.');
  }
  function current() {
    const row = rows.find(x => x.id === selected?.id);
    if (!row) { setLog('REJECT · Hãy Read một issue trước.'); return null; }
    if (row.version !== selected?.version) { setLog('CONFLICT · RowVersion cũ. Bấm lại row để Read bản mới nhất.'); return null; }
    return row;
  }
  function update() {
    const row = current(); if (!row) return;
    if (!title.trim()) { setLog('REJECT · Title không được rỗng.'); return; }
    const next = { ...row, title: title.trim(), status, version: row.version + 1 };
    setRows(rows.map(x => x.id === row.id ? next : x)); setSelected(next); setConfirmDelete(false);
    setLog('POST fmc_UpdatePointIssue → Updated: true · GET lại → RowVersion ' + next.version);
  }
  function remove() {
    const row = current(); if (!row) return;
    if (row.status !== 'Draft') { setLog('REJECT · Only Draft issues can be deleted. Rule kiểm tra ở server.'); setConfirmDelete(false); return; }
    if (!confirmDelete) { setConfirmDelete(true); setLog('Xác nhận xóa bản Draft ' + row.id + ' trong bộ nhớ giả lập?'); return; }
    setRows(rows.filter(x => x.id !== row.id)); setSelected(null); setTitle(''); setStatus('Draft'); setConfirmDelete(false);
    setLog('POST fmc_DeleteDraftPointIssue → Deleted: true.');
  }
  function otherUser() {
    if (!selected) return;
    setRows(rows.map(x => x.id === selected.id ? { ...x, version: x.version + 1 } : x));
    setLog('Tab B vừa sửa row trên server giả lập. Form hiện tại vẫn giữ RowVersion cũ; thử Update.');
  }
  return <>
    <div className="section-title"><div><span className="eyebrow">LOCAL SANDBOX</span><h2>Hiểu CRUD bằng một lần thử.</h2></div><span className="pill">Không kết nối Dataverse</span></div>
    <p className="intro">Dữ liệu chỉ nằm trong bộ nhớ. Rời tab hoặc tải lại sẽ reset. ID ISS-xxx là nhãn mô phỏng, app thật dùng GUID. Dùng nút “Giả lập người khác sửa” để hiểu RowVersion.</p>
    <div className="sandbox">
      <div className="demo-list"><div className="minor-title">POINT TEMP-001 <span>{rows.length} issues</span></div>
        {rows.map(row => <button className={'issue-row ' + (selected?.id === row.id ? 'selected' : '')} key={row.id} onClick={() => read(row)}><span className="issue-top">{row.id}<span className={'status ' + row.status.toLowerCase()}>{row.status}</span></span><strong>{row.title}</strong><small>RowVersion {row.version} · Bấm để Read →</small></button>)}
        {!rows.length && <p>Chưa có issue. Tạo một bản nháp đầu tiên.</p>}
      </div>
      <div className="demo-form"><h3>{selected ? 'Đang đọc ' + selected.id : 'Tạo issue mới'}</h3>
        <label htmlFor="demo-title">Tiêu đề <span>*</span></label><input id="demo-title" placeholder="Ví dụ: Cảm biến nhiệt độ bất thường" maxLength={200} value={title} onChange={e => { setTitle(e.target.value); setConfirmDelete(false); }} />
        <label htmlFor="demo-status">Trạng thái</label><select id="demo-status" value={status} onChange={e => { setStatus(e.target.value); setConfirmDelete(false); }}><option>Draft</option><option>Open</option><option>Resolved</option></select>
        <p className="hint">Create luôn tạo Draft. Update mới lưu trạng thái đã chọn.</p>
        <div className="actions"><button className="primary" onClick={create}>C · Create</button><button disabled={!selected} onClick={update}>U · Update</button><button className="danger" disabled={!selected} onClick={remove}>{confirmDelete ? 'Xác nhận xóa' : 'D · Delete'}</button></div>
        <button className="text-button" disabled={!selected} onClick={otherUser}>↻ Giả lập người khác sửa</button>
        <p className="hint">Form giữ RowVersion: <strong>{selected?.version ?? '—'}</strong>. Read = bấm một row bên trái.</p>
      </div>
    </div>
    <div className="console"><span className="eyebrow">REQUEST LOG · MÔ PHỎNG</span><p role="status" data-testid="demo-log">{log}</p></div>
    <div className="callout tip"><h3>Thử 3 tình huống</h3><ol><li>Nhập tiêu đề → Create → sửa tiêu đề → Update.</li><li>Chọn ISS-002 đang Open → Delete: thấy server giả lập từ chối.</li><li>Chọn một issue → giả lập người khác sửa → Update: conflict → bấm lại row → Update thành công.</li></ol></div>
  </>;
}

function App() {
  const fromHash = location.hash.slice(1);
  const [active, setActive] = useState(lessons.some(x => x.id === fromHash) ? fromHash : 'overview');
  const [tab, setTab] = useState('learn');
  const [search, setSearch] = useState('');
  const [done, setDone] = useState<string[]>(initialProgress);
  const [file, setFile] = useState('store');
  const [menu, setMenu] = useState(false);
  const index = lessons.findIndex(x => x.id === active);
  const lesson = lessons[index];
  const normalize = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase();
  const filtered = lessons.filter(x => normalize(JSON.stringify(x)).includes(normalize(search)));
  function select(id: string) { setActive(id); setTab('learn'); setMenu(false); history.replaceState(null, '', '#' + id); window.scrollTo({ top: 0, behavior: 'auto' }); }
  function toggle(id: string) {
    const next = done.includes(id) ? done.filter(x => x !== id) : [...done, id];
    setDone(next); try { localStorage.setItem(progressKey, JSON.stringify(next)); } catch { /* Session state still works. */ }
  }
  return <div className="app-shell">
    <aside className={'sidebar ' + (menu ? 'is-open' : '')}>
      <a className="brand" href="#overview" onClick={e => { e.preventDefault(); select('overview'); }}><span className="brand-mark">fm<span>c</span></span><span>BUILD HANDBOOK<small>Metasys · Developer series</small></span></a>
      <div className="sidebar-heading">CUSTOM API / 01</div><h2>Point Issue<br/>Tracker<span className="accent-dot">.</span></h2>
      <label className="search-wrap"><span aria-hidden="true">⌕</span><input aria-label="Tìm bài học" placeholder="Tìm bài, code, quyền…" value={search} onChange={e => setSearch(e.target.value)} /><kbd>/</kbd></label>
      <nav aria-label="Mục lục">{filtered.map(item => { const i = lessons.indexOf(item); return <button key={item.id} className={'nav-item ' + (active === item.id && tab === 'learn' ? 'active' : '')} onClick={() => select(item.id)}><span className={'nav-number ' + (done.includes(item.id) ? 'done' : '')}>{done.includes(item.id) ? '✓' : String(i + 1).padStart(2, '0')}</span><span>{item.title}</span></button>; })}{!filtered.length && <p className="empty-search">Không tìm thấy bài phù hợp.</p>}</nav>
      <div className="sidebar-bottom"><div><span>Tiến độ tự học</span><strong>{done.length}/{lessons.length}</strong></div><progress aria-label="Tiến độ tự học" max={lessons.length} value={done.length}/><p>Học từng bước. Tự tay implement.</p></div>
    </aside>
    <div className="main-area">
      <header className="topbar"><button className="menu-toggle" aria-label="Mở mục lục" aria-expanded={menu} onClick={() => setMenu(!menu)}>☰</button><span>FMC BMS Demo <span className="breadcrumb">/</span> <strong>Custom API CRUD</strong></span><span className="local-badge"><i/> DOCS LOCAL · REACT</span></header>
      <main>
        <section className="hero"><div className="hero-copy"><span className="eyebrow">TỪ CODE ĐẾN DATAVERSE</span><h1>Một tính năng thật.<br/><em>Bốn thao tác CRUD.</em></h1><p>Hướng dẫn tự xây Point Issue Tracker ngay trong app demo hiện tại — hiểu từng lớp, viết từng file, kiểm chứng từng bước.</p><div className="hero-meta"><span>10 bài hướng dẫn</span><span>7 mẫu code</span><span>C# + React</span></div></div><div className="hero-card"><div className="hero-card-head"><span className="pulse-dot"/> CURRENT POINT <span>TEMP-001</span></div><div className="reading">28.4 <small>°C</small></div><div className="mini-issue"><span>!</span><div>Nhiệt độ phòng cao<small>Point Issue · Draft</small></div><b>↗</b></div><div className="crud-letters"><span>C <small>Create</small></span><span>R <small>Read</small></span><span>U <small>Update</small></span><span>D <small>Delete</small></span></div><p className="hero-caption">MINH HỌA · KHÔNG PHẢI DỮ LIỆU LIVE</p></div></section>
        <div className="scope-note"><span>ⓘ</span><p><strong>Docs + code mẫu, chưa implement feature.</strong> Bạn tự thêm code theo hướng dẫn. Trang này không ghi Dataverse, không sửa app thật và không gọi sync.</p></div>
        <div className="tabs" role="tablist" aria-label="Chế độ đọc">{[['learn', '01', 'Hướng dẫn'], ['code', '02', 'Code theo file'], ['play', '03', 'Thử CRUD'], ['check', '04', 'Checklist']].map(([id, num, title]) => <button key={id} id={'tab-' + id} aria-controls="tab-content" role="tab" aria-selected={tab === id} className={tab === id ? 'selected' : ''} onClick={() => { setTab(id); setMenu(false); }}><span>{num}</span>{title}</button>)}</div>
        <div id="tab-content" role="tabpanel" aria-labelledby={'tab-' + tab}>
          {tab === 'learn' && <article>
            <div className="section-title"><div><span className="eyebrow">BÀI {String(index + 1).padStart(2, '0')} / 10 · {lesson.tag}</span><h2>{lesson.title}</h2><p>{lesson.subtitle}</p></div><span className="chapter-decoration">{String(index + 1).padStart(2, '0')}</span></div>
            <div className="goal"><span>MỤC TIÊU</span><p>{lesson.goal}</p></div>
            {lesson.blocks.map((block, i) => <section key={i} className={block.tone ? 'callout ' + block.tone : 'lesson-block'}><h3>{block.title}</h3>{block.text && <p>{block.text}</p>}{block.items && <ol className="steps">{block.items.map(text => <li key={text}>{text}</li>)}</ol>}{block.table && <div className="table-wrap"><table><thead><tr>{block.table[0].map(x => <th key={x}>{x}</th>)}</tr></thead><tbody>{block.table.slice(1).map((row, r) => <tr key={r}>{row.map((x, c) => <td key={c}>{x}</td>)}</tr>)}</tbody></table></div>}{block.code && <pre className="inline-code"><code>{block.code}</code></pre>}</section>)}
            {['contract', 'business', 'plugin', 'register'].includes(lesson.id) && <p className="lesson-reference">Đối chiếu: {sources.filter((_, i) => lesson.id === 'business' ? i === 3 : lesson.id === 'register' ? [1, 4, 5].includes(i) : i === 0).map(([title, url]) => <a key={url} href={url} target="_blank" rel="noreferrer">{title} ↗ </a>)}</p>}
            {lesson.samples?.map(id => <Code key={id} sample={samples.find(x => x.id === id)!}/>)}
            <div className="checkpoint"><div><span className="eyebrow">CHECKPOINT</span><p>{lesson.checkpoint}</p></div><label><input type="checkbox" checked={done.includes(active)} onChange={() => toggle(active)}/> Tôi đã kiểm tra</label></div>
            <div className="lesson-pagination"><button disabled={index === 0} onClick={() => select(lessons[index - 1].id)}>← Bài trước</button><button className="primary" onClick={() => index < lessons.length - 1 ? select(lessons[index + 1].id) : setTab('check')}>{index < lessons.length - 1 ? 'Bài tiếp theo →' : 'Mở checklist →'}</button></div>
          </article>}
          {tab === 'code' && <section><div className="section-title"><div><span className="eyebrow">SOURCE RECIPE</span><h2>Code nằm ở đây.</h2><p>Chọn file → đọc ghi chú → copy → tự thêm vào đúng project.</p></div></div><div className="file-picker" role="group" aria-label="Chọn code mẫu">{samples.map(s => <button key={s.id} className={file === s.id ? 'selected' : ''} onClick={() => setFile(s.id)}>{s.title}</button>)}</div><Code key={file} sample={samples.find(x => x.id === file)!}/><div className="callout warning"><h3>Không copy tất cả thành một file</h3><p>C# vào plugin project; React vào src của app. Schema và Custom API registration là bước riêng ở bài 03–07. Các snippet là code đề xuất, chưa compile/deploy như một feature hoàn chỉnh.</p></div></section>}
          {tab === 'play' && <Playground/>}
          {tab === 'check' && <section><div className="section-title"><div><span className="eyebrow">SHIP WITH EVIDENCE</span><h2>Đã làm được, không chỉ đã đọc.</h2><p>Tiến độ này lưu trên trình duyệt; không phải kết quả test tự động.</p></div><span className="pill">{done.length} / 10 hoàn thành</span></div><div className="checklist">{lessons.map((item, i) => <label className="check-item" key={item.id}><input type="checkbox" checked={done.includes(item.id)} onChange={() => toggle(item.id)}/><span><strong>{String(i + 1).padStart(2, '0')} · {item.title}</strong><small>{item.checkpoint}</small></span></label>)}</div><button onClick={() => { setDone([]); try { localStorage.removeItem(progressKey); } catch {} }}>Đặt lại tiến độ</button></section>}
        </div>
        <footer><div><span className="eyebrow">TÀI LIỆU GỐC</span><h3>Đối chiếu với Microsoft Learn</h3><p>Repo kiểm tra ngày 28/09/2026 · Point Issue là đề xuất học tập, không phải deployment receipt.</p></div><div className="source-links">{sources.map(([title, url]) => <a key={url} href={url} target="_blank" rel="noreferrer">{title} ↗</a>)}</div></footer>
      </main>
    </div>
  </div>;
}
render(<App/>, document.getElementById('root'));
