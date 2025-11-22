using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jjevol
{
    public class VMCollection 
    {
        /// <summary>
        /// 데이터 구독 여부 (기본값: true)
        /// </summary>
        protected bool mSubscribe = false;

        /// <summary>
        /// 구독 상태 변경 처리. subscribe 상태가 변경될 경우 true 반환
        /// </summary>
        protected virtual bool _SetSubscribe(bool subscribe)
        {
            if (mSubscribe != subscribe)
            {
                mSubscribe = subscribe;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 기본 생성자 – 구독 상태 활성화
        /// </summary>
        public VMCollection()
        {
            _SetSubscribe(true);
        }

        ~VMCollection()
        {
            Dispose();
        }

        /// <summary>
        /// 해제 처리가 필요한 리소스 정리용 메서드 – 상속 시 오버라이드 가능
        /// </summary>
        public virtual void Dispose()
        {
            _SetSubscribe(false);
        }
    }
}