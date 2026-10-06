import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.dirname(fileURLToPath(import.meta.url));
const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const harness=String.raw`
<script>
let browserChecks=0;
function checkBrowser(condition,message){if(!condition)throw new Error(message);browserChecks++;}
try{
  checkBrowser(document.documentElement.dataset.guideReady==='true','page init');
  checkBrowser(document.querySelectorAll('.scenario-btn').length===16,'16 scenario controls');
  for(const scenario of scenarios){
    document.querySelector('[data-scenario="'+scenario.id+'"]').click();
    checkBrowser($('step-counter').textContent.startsWith('0 /'),'scenario reset');
    for(let n=0;n<scenario.events.length;n++)$('next').click();
    checkBrowser($('next').disabled,'timeline terminal control');
    checkBrowser(Number($('scene-value').textContent)===result.final.scene,'render scene');
    checkBrowser($('step-counter').textContent===scenario.events.length+' / '+scenario.events.length,'progress');
    $('prev').click();checkBrowser(!$('next').disabled,'previous restores next');
    $('reset').click();checkBrowser($('prev').disabled,'reset controls');
  }
  document.querySelector('[data-scenario="native-candidate-full"]').click();
  for(let n=0;n<chosen.events.length;n++)$('next').click();
  checkBrowser($('ledger-value').textContent==='10','ledger display');
  checkBrowser($('inventory-value').textContent==='90','inventory display');
  checkBrowser($('mail-value').textContent==='1 / 1','mail display');
  $('packet-details').open=true;
  checkBrowser($('packet-content').textContent.includes('3505'),'history fixed body display');
  document.querySelector('[data-tab="catalog"]').click();
  checkBrowser(document.querySelectorAll('.protocol-item').length===11,'11 catalog rows');
  $('catalog-search').value='04BB';$('catalog-search').dispatchEvent(new Event('input'));
  checkBrowser(document.querySelectorAll('.protocol-item').length===1,'protocol search');
  document.querySelector('.protocol-item summary').click();
  checkBrowser(document.querySelector('.protocol-item').open,'protocol expansion');
  $('catalog-search').value='';$('catalog-search').dispatchEvent(new Event('input'));
  $('direction-filter').value='C2S';$('direction-filter').dispatchEvent(new Event('change'));
  checkBrowser(document.querySelectorAll('.protocol-item').length===3,'C2S direction filter');
  document.querySelector('[data-tab="rules"]').click();
  $('sandbox-scene').value='8';$('sandbox-stage').value='2';$('sandbox-run').click();
  checkBrowser($('sandbox-result').textContent.includes('scene 8 → 12'),'sandbox fallback');
  $('sandbox-scene').value='13';$('sandbox-run').click();
  checkBrowser($('sandbox-result').textContent.includes('scene 13 → 9'),'sandbox start');
  checkBrowser(!document.body.textContent.includes('\ufffd'),'no replacement glyph');
  checkBrowser(document.documentElement.scrollWidth<=window.innerWidth,'no horizontal overflow');
  document.querySelector('[data-tab="flow"]').click();
  document.querySelector('[data-scenario="native-candidate-full"]').click();
  for(let n=0;n<20;n++)$('next').click();
  const proof=document.createElement('pre');proof.id='browser-test-result';proof.dataset.status='passed';
  proof.textContent=JSON.stringify({status:'passed',checks:browserChecks,scenarios:scenarios.length,viewport:[window.innerWidth,window.innerHeight],boundary:'own HTML in local headless Chrome; not game ClientVerified'});
  document.body.appendChild(proof);
  document.documentElement.dataset.browserTests='passed';
}catch(error){const proof=document.createElement('pre');proof.id='browser-test-result';proof.dataset.status='failed';proof.textContent=error.stack;document.body.appendChild(proof);document.documentElement.dataset.browserTests='failed';}
</script>`;
fs.writeFileSync(path.join(root,'browser-test.html'),html.replace('</body>',harness+'</body>'),'utf8');
console.log('browser-test.html generated (local self-test harness only)');
