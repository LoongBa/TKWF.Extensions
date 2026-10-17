using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x516C;&#x4F17;&#x53F7;&#x6E20;&#x9053;&#x914D;&#x7F6E;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatChannelConfig&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x57FA;&#x7840;&#x914D;&#x7F6E;&#xFF08;DB &#x6743;&#x5A01;&#xFF0C;&#x65B9;&#x6848; &#xA7;3.3.4 &#x4E24;&#x6E90;&#x5E76;&#x5B58;&#xFF1A;DB &#x4F18;&#x5148; &#x2192; Options &#x964D;&#x7EA7;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x1F512; &#x51ED;&#x8BC1;&#x5B89;&#x5168;&#xFF08;Oracle P1 &#x5B89;&#x5168;&#x4E94;&#x8865;&#x5145;&#xFF09;&#xFF1A;&lt;see cref=&quot;AppSecretCipher&quot;/&gt;/&lt;see cref=&quot;TokenCipher&quot;/&gt;/&lt;see cref=&quot;EncodingAESKeyCipher&quot;/&gt;      &#x4E09;&#x5B57;&#x6BB5;&#x4E3A; AES-GCM &#x5355;&#x6BB5;&#x5BC6;&#x6587;&#xFF08;base64(nonce&#x2016;cipher&#x2016;tag)&#xFF0C;&#x5BF9;&#x9F50; AuthSurface V0.2.0 &#x89C4;&#x8303;&#xFF09;&#x2014;&#x2014;&lt;b&gt;&#x660E;&#x6587;&#x6C38;&#x4E0D;&#x843D;&#x5E93;&lt;/b&gt;&#xFF1B;      DTO &#x6295;&#x5F71;&#x4E0D;&#x542B;&#x51ED;&#x8BC1;&#xFF08;&#x5BC6;&#x6587;&#x4E0D;&#x56DE;&#x663E;&#xFF09;&#xFF1B;&#x89E3;&#x5BC6;&#x4EC5;&#x51FA;&#x7AD9;&#x77AC;&#x65F6;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x552F;&#x4E00;&#x7D22;&#x5F15;&#xFF1A;ChannelId&#xFF08;&#x516C;&#x4F17;&#x53F7;&#x5B9E;&#x4F8B; id&#xFF09;/AppId&#xFF08;&#x516C;&#x4F17;&#x53F7; AppId&#x2014;&#x2014;&#x51FA;&#x7AD9;&#x51ED;&#x8BC1;&#x89E3;&#x6790;&#x952E;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 WeChatChannelConfigEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class WeChatChannelConfigEntityDataService(IDomainUser user, IEntityDAC<WeChatChannelConfigEntity> dac)
        : DomainDataServiceBase<WeChatChannelConfigEntity, WeChatChannelConfigEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}